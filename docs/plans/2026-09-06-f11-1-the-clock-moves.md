# F11-1 — the clock moves

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** Every task message the bot sends — the capture reply and the fired reminder alike —
carries one button today: `[Done]`. The owner wants a Schedule affordance next to it, so a
reminder can be pushed to a later time without retyping the task. The full feature (spec §6.4)
describes a `[Done] [Schedule]` pair where Schedule opens a preset menu (`+1h`, `+3h`,
`Tonight 20:00`, `Tomorrow 09:00`, `Back`). That menu is F11-2. This slice, F11-1, ships the
smallest thing that proves the whole chain end to end and is independently verifiable on a real
phone: the keyboard becomes two flat buttons, `[Done] [+1h]`. Tapping `+1h` moves the task's due
time to one hour from now, clears its reminder-sent marker, and rewrites the message in place to
show the new time — the chat is a truthful list of open tasks, so a stale due time left on screen
would be a lie. `SnoozeAction`, `RescheduleAction`, and `EditAction`, as spec §6.4 originally named
three separate actions, are not built: the owner has ruled snooze and reschedule are one operation,
and `EditAction` is dropped entirely now that F10-4 deletes the owner's own message on a successful
capture — there is no message left to edit in place, so retyping the task is the edit.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **no new NuGet package** and **no database migration**: `ReminderTask.DueAt` and
`ReminderTask.ReminderSentAt` are both already nullable columns, present since the very first
migration (see "Verified facts").

**Spec:** `docs/design/slice-1-reminders.md` §6.4 (inline buttons — corrected in the same commit,
see Decision 15), §4.2 (`TaskService` as single writer — the `RescheduleAsync` bullet is corrected
in the same commit, see Decision 15), §7.2 (unit vs. integration split), §7.3 (assertion standard),
§12.1 (XML docs), §12.5 (primary constructors), §12.6 (no emoji).

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — the F11 entry (currently line
633, `**F11 · Snooze and reschedule**`) and the deferred-properties table's `DeliveryAttempts` row
(currently line 754). Both are corrected in the same commit, per Decision 15.

**Issue:** GitHub issue #27, "An undated task is stored but can never fire," read in full via
`gh issue view 27`. It is not closed by this slice — see Decision 9 and "What this slice does NOT
include" — but `+1h` on an undated task is the first thing that gives it a due time at all, which
the backlog correction records.

---

## How this slice fits

Today `TelegramNotifier.SendTaskAsync` builds one button by hand — the catalogue's `Done` entry —
and its own `<remarks>` say plainly that iterating `TaskActions.All` would silently fix the layout
at one row, "a decision that belongs to F11." F11-1 is that decision, made for exactly two buttons:
`[Done] [+1h]`, one row. Reaching it touches more of the codebase than its own size suggests,
because three seams the project already uses for exactly this kind of growth all have to move
together in one build:

1. **`ITaskAction.ExecuteAsync` gains an argument.** `CallbackCodec`'s wire format already reserves
   a fourth, optional segment for this (spec §6.4); nothing has read it until now.
2. **`ITaskService.CompleteAsync` starts returning the task, not a bare `Result`,** and gains
   `RescheduleAsync` with the identical shape — both so `CallbackRouter` can render from the
   result of *any* action without asking which one ran.
3. **`CallbackRouter`'s rendering switches from "the action was Done" to "the task is now
   `Completed`."** This is the load-bearing design decision of the slice — see Decision 10 — and
   it is why the router, `INotifier`, `TelegramNotifier`, `ITaskAction`, and `ITaskService` all
   change in the same commit: none of them compiles against the others' old shape.

**The measured total, drafted in full, built, and tested green, not estimated.** Every file below
was written out completely and diffed against the real working tree — the eighteen `src`/`tests`
files with `git diff --cached --numstat` inside an isolated worktree carrying these exact changes
(built and tested green there twice, from two different starting sequences — see "Verified
facts"), the three documentation files with `git diff --no-index --numstat` against scratch
copies:

| File | + | - |
| :--- | ---: | ---: |
| `src/Assistant.Contracts/ErrorCode.cs` | 5 | 0 |
| `src/Assistant.Contracts/TaskActions.cs` | 21 | 5 |
| `src/Assistant.Interfaces/ITaskAction.cs` | 18 | 4 |
| `src/Assistant.Interfaces/ITaskService.cs` | 29 | 2 |
| `src/Assistant.Interfaces/INotifier.cs` | 23 | 0 |
| `src/Assistant.Impl/Services/TaskService.cs` | 28 | 4 |
| `src/Assistant.Impl/Services/Actions/DoneAction.cs` | 5 | 1 |
| `src/Assistant.Impl/Services/Actions/ScheduleAction.cs` (new) | 44 | 0 |
| `src/Assistant.Impl/Telegram/CallbackCodec.cs` | 34 | 12 |
| `src/Assistant.Impl/Telegram/CallbackRouter.cs` | 36 | 7 |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | 36 | 24 |
| `src/Assistant.Impl/Telegram/MessageHandler.cs` | 1 | 8 |
| `src/Assistant.Impl/Telegram/ReminderTaskTextExtensions.cs` (new) | 35 | 0 |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | 9 | 6 |
| **Code subtotal** | **324** | **73** |
| `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs` | 48 | 9 |
| `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs` | 83 | 0 |
| `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` | 103 | 1 |
| `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` | 10 | 9 |
| `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs` | 11 | 7 |
| **Test subtotal** | **255** | **26** |
| **Code + test total** | **579** | **99** |
| `docs/design/slice-1-reminders.md` | 11 | 6 |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | 30 | 6 |
| `docs/e2e-local.md` | 9 | 0 |
| **Docs subtotal** | **50** | **12** |
| **Grand total** | **629** | **111** |

**Total changed lines: 740** (629 insertions + 111 deletions), 260 lines under the 1000-line
budget. No individual file approaches even `TelegramNotifier.cs`'s own 60-line change.

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** `ScheduleAction(ITaskService
  taskService, TimeProvider timeProvider)`, `CallbackRouter(TelegramSettings settings,
  ITelegramBotClient bot, INotifier notifier, ILocalTimeResolver clock, IEnumerable<ITaskAction>
  actions)` — no separate constructor anywhere.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- Central package management; no inline `Version=`. Not exercised this slice — no package changes.
- No emoji anywhere: source, tests, docs, or commit messages.
- Enums: first member is `Unknown`, no explicit numeric values, new members **appended**, never
  inserted. `ErrorCode.TaskActionArgumentUnrecognized` (Decision 6) is appended after the existing
  final member, `ModelNamedUnknownTool`.
- **Never run `docker compose down -v`.** Use `docker compose -f compose.test.yaml down` with no
  flags, and only if something needs stopping — this slice needs no image rebuild (Decision 12), so
  ordinarily nothing needs to be brought up or down beyond what is already running.
- PR budget: 1000 changed lines per PR, excluding the plan document. This slice measures at 740
  lines by the convention above — comfortably under budget.
- Plain ASCII `--` in every C# file (source, tests, doc comments, code comments); real em dashes in
  the markdown prose of documents that already use them (`slice-1-reminders.md`,
  `2026-08-22-slice-1-feature-backlog.md`, `e2e-local.md` — confirmed by grepping each for the `—`
  character before drafting a single addition).
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was checked directly against the working tree at `b75c76b` (HEAD of
`feature/f11-1-the-clock-moves`, F10-4's own merge commit) or produced by a command actually run
during this planning session, in an isolated `git worktree` that never touched this working tree's
`src/` or `tests/`.

- **No migration is needed.** `src/Assistant.Models/ReminderTask.cs` declares `public
  DateTimeOffset? DueAt { get; set; }` and `public DateTimeOffset? ReminderSentAt { get; set; }` —
  both already nullable. `src/Assistant.Repository/Migrations/20260822103957_InitialCreate.cs` is
  where both columns were first created; the two migrations since
  (`20260822202918_AddDueReminderIndex`, `20260902181436_AddCompletedAt`) touch neither. There is
  no `Priority`, `Notes`, or `DeliveryAttempts` column on the model today.
- **`ITaskAction`'s only real call site is `CallbackRouter.cs:99`,** `await action.ExecuteAsync(taskId,
  ct)`, and its only implementation is `DoneAction`. `ITaskService.CompleteAsync`'s only call site
  is `DoneAction.cs:17`. Both are exhaustively enumerated by `grep -rn "\.ExecuteAsync("` and
  `grep -rn "CompleteAsync"` across `src/` and `tests/` — there is no third caller anywhere, so
  changing both signatures has a fully known blast radius.
- **`DueReminderJob.cs:33` calls the same `notifier.SendTaskAsync(task.Id, task.Title, ct)`
  `MessageHandler` calls on capture.** The two-button keyboard this slice builds therefore reaches
  a *fired* reminder exactly the same way it reaches a capture reply — one change in
  `TelegramNotifier`, both call sites get it for free, with no code change to either caller.
- **Three existing tests assert an exact single-button row and will fail the moment `TelegramNotifier`
  renders two:** `DueReminderJobTests.cs`'s `RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask`
  (`Assert.Single(row)` at line 90) and `TelegramListenerTests.cs`'s
  `Listener_OwnerSendsAMessageWithADueTime_...` and `Listener_OwnerSendsAMessageWithNoDueTime_...`
  (`Assert.Single(row)` at lines 115 and 141). All three were found by grepping the whole test tree
  for `InlineKeyboard`/`TaskActions\.` — not mentioned anywhere in the brief for this slice — and
  all three needed updating for the suite to pass; see Decision 13.
- **One existing unit test asserts a four-segment callback string must fail to decode.**
  `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs`'s
  `TryDecode_MalformedOrUnsupportedStrings_Fails` theory carries
  `[InlineData("v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h")]` — exactly the shape this slice makes valid.
  This row has to be replaced, not merely left alone (see Decision 14), or the suite is
  self-contradictory: one test demanding the four-segment form decode successfully, another
  demanding an example of the same shape fail.
- **`tests/Assistant.WireMock/TelegramStubs.cs`'s `/bot*/editMessageText` mapping already exists**
  (added at F6-3, kept at F10-4) and answers a fixed envelope regardless of the request body —
  `WireMock.RequestBuilders.Request.Create().WithPath("/bot*/editMessageText").UsingPost()` matches
  on path and method only, never on the `reply_markup` field the request carries. `INotifier`'s new
  `UpdateTaskAsync` method (Decision 11) calls the exact same `Telegram.Bot`
  `EditMessageText(...)` extension method `MarkCompletedTaskAsync` already calls, just with a
  different keyboard argument — the wire path is identical. **Consequently this slice needs no new
  WireMock stub mapping and no `docker compose -f compose.test.yaml up -d --build`: a plain `up -d`
  (or nothing, if the stub is already running) suffices**, because `TelegramStubs.cs` is not
  touched. This directly contradicts the general caution in the brief to check for a missing
  mapping — in this specific case, the mapping was already there.
- **`InlineKeyboardMarkup`'s constructor overloads, verified against the installed
  `Telegram.Bot` 22.10.2.1 package twice — once by reading its own XML documentation, once by
  actually running code against it, not by guessing.** The package's
  `lib/net6.0/Telegram.Bot.xml` documents
  `InlineKeyboardMarkup(InlineKeyboardButton[])` as "Creates an InlineKeyboardMarkup with multiple
  buttons on one row." A throwaway console project referencing `Telegram.Bot` 22.10.2.1 then built
  two real buttons and serialised `new InlineKeyboardMarkup(new[] { done, schedule })`:

  ```
  {"inline_keyboard":[[{"Text":"Done",...,"callback_data":"v1:done:...=="},
                        {"Text":"+1h",...,"callback_data":"v1:schedule:...==:+1h"}]]}
  ```

  One row, both buttons, in the order given — exactly the `[Done] [+1h]` layout this slice needs.
  The same throwaway project also re-confirmed the existing `NoButtons` comment's claim:
  `new InlineKeyboardMarkup(Array.Empty<InlineKeyboardButton>())` serialises to
  `{"inline_keyboard":[[]]}`, one empty row, never the empty-keyboard shape `new
  InlineKeyboardMarkup()` (no arguments) produces.
- **Baseline test counts on this exact HEAD, run directly, not assumed.** `dotnet build --no-restore`
  — `Build succeeded. 0 Warning(s). 0 Error(s).` `dotnet test tests/Assistant.UnitTests --no-build`
  — **57 passed**, 0 failed. Against the already-running `personal-ai-assistant-postgres-test-1`
  and `personal-ai-assistant-wiremock-1` containers (both `Up ... (healthy)` throughout this
  planning session — compose.test.yaml's own fixed ports, no `--build` needed since nothing in
  `tests/Assistant.WireMock/` changes), `dotnet test tests/Assistant.IntegrationTests --no-build`
  — **70 passed**, 0 failed. (This is the post-F10-4 baseline; F10-4's own plan quoted 68, the
  number before its own two tests landed.)
- **This plan's own draft was built and tested green, twice, from two different orderings, not
  merely written.** First, every file below was applied in full inside one isolated worktree,
  restored, and built and tested: **0 Warning(s), 0 Error(s)**, **59 unit / 78 integration passed**.
  Second, in a separate isolated worktree, the exact step sequence below (additive pieces first,
  then every test this slice needs, then the interconnected implementation) was replayed and every
  intermediate build/test result quoted in the Steps section — including the real compiler
  errors — was captured from that actual run, not composed from memory. Both worktrees reached the
  identical final state: **59 unit / 78 integration passed**, repeated three times for the seven
  Schedule/Reschedule-focused tests in isolation with no flakiness (**7 passed**, 0 failed, all
  three runs).
- **A real, expected regression appears mid-sequence and is worth naming rather than glossing
  over.** Adding `TaskActions.Schedule` to the catalogue (Step 2) without yet registering
  `ScheduleAction` in DI makes `CallbackRouterTests.cs`'s own
  `ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly` fail — confirmed by actually
  stashing every test-file change and running the integration suite at that exact point: **69
  passed, 1 failed**, naming exactly that test. It stays red until Step 6 registers `ScheduleAction`
  and stays that way through Step 5 (which does not touch this test). This is intentional, not a
  planning error: the test exists precisely to catch a catalogue entry with no implementation, and
  it does its job the moment there is one to catch.
- **GitHub issue #27, read in full via `gh issue view 27`.** It is `OPEN`, filed against F10-3's
  own plan, and names two separate consequences of an undated task being a valid, permanently-
  pending row: the reply must say plainly that no reminder will fire (already done at F10-3), and
  nothing until "F11's `update_task`" gives an undated task a time. `+1h` on an undated task is the
  first thing that does — see Decision 9 — but the issue is not closed by this slice, since there
  is still no listing surface; only a button on the task's own capture message reaches it.

---

## Inherited context: what this slice reads from earlier features

`CallbackCodec`'s `v1:` prefix and three-segment shape (F6-2) are extended, not replaced — every
button already in the owner's chat history keeps decoding. `TaskActionDefinition`'s three-property
shape (F6-2) is reused unchanged for `TaskActions.Schedule`; nothing about the record itself
changes. `CallbackRouter`'s owner-check, callback-answer-in-every-branch, and
old-message-carries-no-text handling (F6-2, F6-3) are read and extended, not rewritten — only the
success branch's rendering choice changes. `TelegramNotifier`'s `Escape`/`NoButtons`/HTML-parse-mode
machinery (F6-3, F10) is reused unchanged; only how the keyboard argument is built changes.
`MessageHandler`'s tool-dispatch and reply-rendering flow (F10) is consumed as-is except for the
one-line extraction in Decision 8. `ITaskService`'s existing four methods (F5a, F6-1, F10-1) are
read, not modified, except `CompleteAsync`'s return type. The owner's own message being deleted on
a successful capture (F10-4) is why `EditAction` needs no follow-up-message routing to build at
all — see the Goal section above.

---

## Decisions

### 1. `CallbackCodec.TryDecode` accepts three or four segments; `Encode` gains a second overload, not a default parameter

**Decision:** `TryDecode`'s signature gains a fourth `out string argument` parameter and accepts
either three or four colon-separated segments, yielding `argument = string.Empty` for the
three-segment form. `Encode` keeps its existing two-argument form unchanged and gains a **second,
three-argument overload** — `Encode(string action, Guid taskId, string argument)` — rather than a
single method with `string argument = ""`.

**Why a second overload, not a default parameter, argued rather than assumed.** A default
parameter would make `Encode("done", id)` and `Encode("done", id, "")` look almost identical at
the call site while producing genuinely different wire strings — the first omits the fourth
segment entirely, and if the second also omitted it whenever the argument was empty, then a
future caller who deliberately wants a four-segment string with an empty argument (unlikely today,
but not impossible once F11-2 adds several presets) would have no way to ask for it. Two named
overloads make the two wire shapes explicit at the call site instead of implicit in a runtime
value, and match the brief's own framing exactly: `Encode` "needs an argument-carrying form" — a
form, not a parameter.

**The `<remarks>` correction.** The existing comment says `TryDecode` "has no notion yet of the
optional trailing `:<arg>` segment spec 6.4 also describes; nothing produces or consumes one until
an argument-taking action arrives." That argument-taking action is `ScheduleAction`, arriving in
this same commit, so the comment is rewritten to describe the accepted shape in the present tense
and to name `ScheduleAction` as the first consumer (see the full file body in Step 22).

### 2. `ITaskAction` is modified in place, not given a second interface

**Decision:** `ExecuteAsync` becomes `Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string
argument, CancellationToken ct)`. No `IArgumentTakingTaskAction` or similar sibling interface is
introduced.

**Why, restated from the project's own established posture, not invented for this slice.**
`ITaskService` and `INotifier` have both grown by modifying an existing method's shape or adding a
method to the existing interface every time a new capability arrived (`CreateAsync`'s addition at
F10-1, `MarkCompletedTaskAsync`'s addition at F6-3) — never by adding a second, narrower interface
for the new capability alone. `ITaskAction` has exactly one implementation-selecting mechanism,
`Definition.Key`, and a caller (`CallbackRouter`) that already treats every registered action
identically; splitting into "actions with an argument" and "actions without" would require
`CallbackRouter` to know which interface a given key belongs to before it could call anything,
reintroducing exactly the per-action branching Decision 10 spends its whole argument avoiding.
`DoneAction` ignores the new parameter; nothing about its own behaviour changes.

**The `<remarks>` correction.** The interface's own comment currently says "snooze, reschedule and
edit actions follow at F11, each adding one more implementation rather than changing this one" —
wrong on two counts now: there is one action, not three, and this interface *is* changed, not left
alone. The corrected text says so directly and cites the same growth posture `ITaskService` and
`INotifier` already demonstrate (full text in Step 6).

### 3. `ITaskService.CompleteAsync` returns the task; `RescheduleAsync` is new with the identical shape

**Decision:**

```csharp
Task<Result<ReminderTask>> CompleteAsync(Guid id, CancellationToken ct);
Task<Result<ReminderTask>> RescheduleAsync(Guid id, DateTimeOffset dueAtUtc, CancellationToken ct);
```

`RescheduleAsync` takes an already-resolved UTC instant, exactly as `CreateAsync` already takes an
already-resolved `dueAtUtc` — resolving a preset or a local time to that instant is entirely the
caller's job (`ScheduleAction`'s, in this slice). It sets `DueAt`, clears `ReminderSentAt` to
`null`, and stamps `UpdatedAt`, the same three-line body shape `CompleteAsync` already has for its
own three fields.

**What happens on an already-completed task, argued both ways as the brief asks.** The decision:
refuse with `ErrorCode.TaskAlreadyCompleted`, the same code and the same check
(`task.Status == ReminderStatus.Completed`) `CompleteAsync` already uses.

*The case for refusing.* A completed task's own message has already lost its keyboard
(`MarkCompletedTaskAsync` sends an explicit empty one) — the only way `RescheduleAsync` is ever
reached for a completed task is a stale button, one still showing `[Done] [+1h]` in chat history
from before the owner tapped Done on it, or a deliberately crafted callback string. Reopening a
task the owner has already marked done, silently, because of a tap on dead UI, is a worse outcome
than a "that button is no longer valid"-flavoured refusal — it would resurrect a task the owner
believes is finished, with no record of why.

*The case for allowing it (rejected).* One could argue rescheduling is strictly less destructive
than completing — it only moves a date, it does not discard the task's completion record the way
a second `CompleteAsync` call would overwrite `CompletedAt` if it did not refuse. But "less
destructive" is not "harmless": a task silently reopening itself because of an old button is a
correctness surprise regardless of how small the mutation is, and the project's own precedent
(`CompleteAsync`'s existing refusal) already establishes that a completed task is a terminal state
buttons cannot reach back into. Consistency with that precedent, not a fresh argument, settles it.

**Why `RescheduleAsync` needs no `DueTimeMissing`-style guard the way `MarkReminderSentAsync`
does.** `MarkReminderSentAsync` refuses a `null` `DueAt` because there is no reminder to have been
delivered. `RescheduleAsync` is the opposite case by design: a `null` `DueAt` is exactly the
situation `+1h` on an undated task is meant to fix (Decision 9), so accepting it and setting a
`DueAt` for the first time is the correct behaviour, not an edge case to reject.

### 4. `ScheduleAction`'s "now" comes from `TimeProvider`, never from `ILocalTimeResolver`

**Decision:** `ScheduleAction(ITaskService taskService, TimeProvider timeProvider)`. `+1h`
resolves to `taskService.RescheduleAsync(taskId, timeProvider.GetUtcNow().AddHours(1), ct)`. No
`ILocalTimeResolver` dependency exists on this type at all.

**The correctness point the brief names, argued precisely.** Adding an hour to a *wall-clock
reading* can be wrong across a daylight-saving boundary — Jerusalem's spring-forward night has an
hour that repeats zero times and its fall-back night has one that repeats twice, so "the reading
one hour later" is not always "the instant one hour later." Adding an hour to a *UTC instant* has
no such failure mode: `DateTimeOffset.AddHours` operates on the underlying tick count, which is
already zone-free, so the result is always exactly 3,600 real seconds later regardless of what any
zone's clocks are doing at that moment. `TimeProvider.GetUtcNow()` returns exactly such an instant.

**Why this is correct by construction, not merely correct today.** A version of this type that
took `ILocalTimeResolver.CurrentLocalTime` (a wall-clock reading) and added an hour to *that*
before handing it anywhere would be a live bug waiting for the next DST boundary. `ScheduleAction`
as designed cannot make that mistake, because it has no wall-clock reading in scope to make it
with — there is no `ILocalTimeResolver` field to reach for. This is why F11-1 needs no resolver
dependency at all, exactly as the brief anticipates, and it is a stronger guarantee than a code
comment: the type signature itself rules the bug out.

**No dedicated DST test is added for this reason.** A test asserting "`+1h` across a DST boundary
still lands exactly one hour later" would only be interesting if the implementation route were
capable of getting it wrong — resolving through `ILocalTimeResolver`. Since `ScheduleAction`
structurally cannot take that route, such a test would only re-prove `DateTimeOffset.AddHours`'s
own correctness, an implementation detail of the base class library, not a business rule this
project owns. `AGENTS.md`'s "no unit test for behaviour an integration test already covers" and
the broader "test business use cases, never implementation details" rule both argue against
adding one.

**Measured from now, not from the task's own due time — argued, matching the brief's own lean.**
The owner taps `+1h` because they are busy *right now*; the common case for this tap is a reminder
that has already fired, so its `DueAt` is already in the past. Adding one hour to a past `DueAt`
would produce an instant that may itself already be in the past or only minutes away — the
reminder would fire again almost immediately, defeating the entire point of snoozing it. Measuring
from `timeProvider.GetUtcNow()` instead means `+1h` always means what it says: one hour from the
moment of the tap.

**`+1h` on a task with no due time works, and is the real fix for half of issue #27.** Nothing in
`ScheduleAction` or `RescheduleAsync` requires an existing `DueAt` — `RescheduleAsync` simply sets
one. An owner who captured "remember to call the bank" with no time, then later taps `+1h` on its
message, gives it a due time for the first time. This is mentioned in the backlog correction
(Decision 15); the issue itself is not closed, since there is still no listing surface to find such
a task through other than its own capture message.

### 5. An unrecognised argument is refused with a new `ErrorCode`, not silently accepted or thrown

**Decision:** `ScheduleAction.ExecuteAsync` compares `argument` to the literal `"+1h"` and, on any
other value, returns `Result<ReminderTask>.Failure(ErrorCode.TaskActionArgumentUnrecognized)`
without ever calling `RescheduleAsync` or reading the task from the repository.

**Not settled by the brief; my own call, argued by precedent already in this codebase.** The brief
specifies F11-1 "understands exactly one argument value, `+1h`" but does not say what happens for
any other value. The precedent that answers it is `ErrorCode.ModelNamedUnknownTool`, added at
F10-3 for the structurally identical situation one layer up — the chat model naming a tool that
looks plausible but is not registered. `ModelNamedUnknownTool`'s own filing (issue #28) reasons
that this becomes *more* likely once several similarly-named things exist side by side; the same
reasoning applies here the moment F11-2 adds `+3h`, `tonight`, and `tomorrow` as sibling argument
values a stale button from an in-between build might still send. Refusing cleanly now, with a code
that names the failure precisely, costs four lines and means F11-2 inherits a working fallback
rather than needing to invent one under time pressure.

**Why not reuse an existing code.** `TaskNotFound` would be actively misleading — the task named
by the button may well exist; it is the *argument* that is wrong, checked and refused before the
repository is ever touched. `DueTimeUnparseable` is a different subsystem's failure
(`ILocalTimeResolver.Resolve` parsing free text), not a button-argument mismatch, and reusing it
would make a future reader believe this path goes through the time resolver when it does not.

**The router's mapping.** `CallbackRouter`'s existing three-arm `switch` on the result gains a
fourth: `{ Error: ErrorCode.TaskActionArgumentUnrecognized } => ThatButtonIsNoLongerValid` — the
same string already used for a malformed or unregistered callback, since from the tapper's side
these are indistinguishable: a button this build does not know what to do with.

### 6. `ErrorCode.TaskActionArgumentUnrecognized` is appended, not inserted

Per §12.7's enum rule: appended after the current final member, `ModelNamedUnknownTool`. Named
generically — `TaskAction`, not `Schedule` — because `ITaskAction.ExecuteAsync`'s new `argument`
parameter is generic to every action, and a future action rejecting its own unrecognised argument
should reuse this code rather than mint a sibling.

### 7. No migration; verified, not assumed

`ReminderTask.DueAt` and `ReminderTask.ReminderSentAt` are both nullable already, present since
`InitialCreate` (see "Verified facts"). `RescheduleAsync` only ever assigns to columns that already
exist and already accept the values it assigns. `dotnet ef migrations add` is not run anywhere in
this plan.

### 8. A shared due-time renderer: an extension method on `ReminderTask`, not a small type

**Decision:** `internal static class ReminderTaskTextExtensions` in `Assistant.Impl.Telegram`, with
one method, `ToMessageText(this ReminderTask task, ILocalTimeResolver clock)`, holding the
`DueTimeFormat` constant and the exact ternary `MessageHandler` builds inline today.

**The rejected alternative: a small type, e.g. `TaskMessageRenderer`, injected as a dependency.**
Rejected because there is no state to own and no second implementation this project has any use
for — the rendering rule is a pure function of a task and a clock, which is exactly what an
extension method expresses with no ceremony. Introducing a class with a constructor and a DI
registration for one pure function would be machinery for a plurality that does not exist, the
same YAGNI reasoning `TelegramNotifier`'s own existing `<remarks>` already applies to
`TaskActions.All`.

**Why `Assistant.Impl.Telegram`, not `Assistant.Impl.Mapping`.** `AGENTS.md`/spec §12.2 reserve
`Impl/Mapping` for mapping between `Models`, `Contracts`, and `Interfaces` shapes — crossing a real
boundary. This method renders a `Models` type into a Telegram-specific display string; both of its
only two callers, `MessageHandler` and `CallbackRouter`, already live in `Impl/Telegram`, so that
is where the shared logic goes too.

**`MessageHandler`'s own change is a pure extraction, not a refactor of its flow.** The method's
body changes from a four-line inline ternary to `await notifier.SendTaskAsync(task.Id,
task.ToMessageText(clock), ct);` — the exact same string, byte for byte, for the exact same
inputs. No existing `MessageHandler`-facing test needs a single assertion changed by this step;
see Step 4's own expected result. This is deliberately the only touch this slice makes to
`MessageHandler.HandleAsync`, matching "no broader refactor of `MessageHandler`."

### 9. `+1h` on an undated task is meaningful and is left open as issue #27's

Already argued in Decision 4's own final paragraph. Restated here because it is a scope boundary,
not an implementation detail: this slice makes the behaviour *possible*, and the backlog
correction (Decision 15) records that it does — but no listing feature, no `/status` surfacing of
undated tasks, and no change to issue #27's own open state are in scope. Closing the loop it names
(discoverability) is explicitly deferred.

### 10. The router renders by the task's resulting `Status`, never by which action ran

**This is the central design decision of the slice, argued at length as the brief asks.**

**The problem a second action creates.** Today `CallbackRouter.HandleAsync`'s success branch is
`if (result.IsSuccess && messageText is not null) { await
notifier.MarkCompletedTaskAsync(messageId, messageText, ct); }` — it assumes every successful
action is Done, because until now every successful action *was* Done. `ScheduleAction` breaks that
assumption: a successful reschedule must not be struck through, and it needs to show new text
Done's rendering has no notion of.

**Two rejected shapes, each looking obvious enough to reach for first.**

*A switch on the action's key* (`if (actionKey == "done") { ... } else if (actionKey ==
"schedule") { ... }`) is rejected because it makes `CallbackRouter` the one place that has to know
about every action that will ever exist, forever — the opposite of the extension seam spec §3.6
already establishes for `ITaskAction` ("new button → new class, resolved by callback key"). Every
future action (F11-2's remaining presets reuse `ScheduleAction`, but a genuinely new action later
would not) would need a new arm here, in a file whose whole reason for existing is to *not* need
editing when a new action arrives.

*A display-intent enum on `TaskActionDefinition`* (e.g. `RenderKind.StrikeThrough` vs
`RenderKind.Rewrite`) is rejected because it duplicates information the task itself already
carries more reliably: `ReminderTask.Status`. An action's *definition* is static — declared once,
at startup, the same for every task it is ever invoked against. Whether a given invocation should
render as "done" is not static; it is a fact about that one task, after that one call, which only
the returned `ReminderTask` can state. A `RenderKind` on the definition would in practice always
just mirror "did this action typically produce a completed task," which is exactly the
information `task.Status` already carries first-hand, one level closer to the truth, with no
possibility of drifting out of sync with what the action actually did.

**The chosen shape.** `ITaskAction.ExecuteAsync` returns the task (Decision 2, Decision 3).
`CallbackRouter` reads `result.Value!.Status`: `Completed` strikes through and clears the keyboard
via `MarkCompletedTaskAsync`, exactly as before; anything else re-renders the message in place via
the new `UpdateTaskAsync` (Decision 11), with text rebuilt fresh from the task via
`ToMessageText(clock)` (Decision 8) and the same task keyboard `SendTaskAsync` would have built.
`CallbackRouter` never names `TaskActions.Done` or `TaskActions.Schedule` anywhere in its own body
— only in its `<remarks>`, as prose. A future action inherits correct rendering automatically: if
it completes the task, it renders as completed; otherwise it renders as still open, with whatever
text and keyboard the task's own state implies at that moment.

**The message-too-old case, re-examined under the new shape, not merely preserved.** The existing
guard — `Message.Text` bound with a plain `var`, since Telegram omits it for messages it judges too
old — still protects `MarkCompletedTaskAsync`'s call, which genuinely needs the *previous* on-screen
text to strike through. It does **not** guard the new `UpdateTaskAsync` call, because that call
needs no previous text at all — it builds the whole message fresh from the task. This is a real
behavioural difference from Done's own rendering, stated explicitly in the `<remarks>` (Step 22),
not an oversight: a reschedule can be re-rendered even for a message Telegram would refuse to hand
back the old text of, precisely because nothing here depends on that old text.

### 11. `INotifier` grows `UpdateTaskAsync`, distinct from `MarkCompletedTaskAsync`

**Decision:**

```csharp
Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct);
```

**Shape argued against `INotifier`'s own stated contract, not merely added.** The interface's
`<remarks>` say a task identifier "is the channel-neutral handle an adapter needs to build
whatever affordance its own channel supports," and that rendering the body is the caller's job.
`UpdateTaskAsync` follows both exactly: it takes plain, unescaped `text` (the caller already built
it via `ToMessageText`) and `taskId` (so the adapter can rebuild its own channel's keyboard for
that task, the same handle `SendTaskAsync` already takes) — never a model, never a
pre-built `InlineKeyboardMarkup`, which would leak Telegram's own wire shape into a
channel-neutral interface.

**Why a new method, not an optional-keyboard parameter added to `MarkCompletedTaskAsync`.**
`MarkCompletedTaskAsync`'s own contract is unconditional: it always clears the keyboard, because a
completed task always has nothing left to act on. Growing it with a "keep the keyboard or not"
flag would turn one method into two behaviours selected by a boolean, which is exactly the kind of
ambiguous signature this project's own conventions (primary constructors, explicit mapping
methods) consistently avoid in favour of two named things.

### 12. `TelegramNotifier` builds both buttons in one row by hand; the new stub-rebuild caution does not apply

**Decision:** `SendTaskAsync` and the new `UpdateTaskAsync` both call a private
`BuildTaskKeyboard(Guid taskId)` that constructs `new InlineKeyboardMarkup(new[] { doneButton,
scheduleButton })` — the two-argument array-constructor overload verified in "Verified facts" to
lay both buttons in a single row.

**Why not iterate `TaskActions.All`, now that it finally has more than one entry.** The existing
`<remarks>` predicted this exact moment — "a decision that belongs to F11" — but the arrival of a
second entry does not, on its own, make iteration the right shape, because `Schedule`'s callback
data needs a *specific preset argument*, `"+1h"`, encoded into it, and that argument is a property
of *which button is being drawn*, not of the action definition itself. `TaskActionDefinition` has
no argument field, and giving it one now would mean inventing "one argument per definition" to
serve a single caller, when F11-2's whole point is that one action (`Schedule`) will back *several*
buttons with *different* arguments (`+1h`, `+3h`, `tonight`, `tomorrow`) — a generic per-definition
argument would already be the wrong shape for that. Building the two buttons by hand, as before,
defers that design question to F11-2, which actually needs to answer it, rather than answering it
speculatively now.

**The stub-image caution in the brief does not apply here.** The brief's own caution is to check
whether `TelegramStubs.cs` already stubs whatever endpoint the new behaviour needs, and to add the
mapping plus rebuild the image if not. "Verified facts" confirms `/bot*/editMessageText` is already
stubbed, unconditionally on request body, since F6-3 — `UpdateTaskAsync` reaches that exact
existing mapping. `TelegramStubs.cs` is not touched by this slice, so no rebuild is needed.

### 13. Three existing tests needed fixing for an unrelated reason: they hard-coded "exactly one button"

Found by grepping the whole test tree for `InlineKeyboard`/`TaskActions\.`, not named anywhere in
the brief. `DueReminderJobTests.RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask` and
`TelegramListenerTests`'s two capture-reply tests all asserted `Assert.Single(row)` — exactly one
button in the row. Once `TelegramNotifier` renders two, all three fail, not because anything about
*their own* subject (which button carries which callback data, whether a due time renders
correctly) changed, but because the row itself grew. All three are updated to assert both labels
and both callback-data strings, in order, rather than dropped or weakened — the claim "the Done
button is still exactly right" is worth exactly as much after this slice as before it, and is kept
by asserting on the button's own index in the row rather than on the row's length.

### 14. `CallbackCodecTests`'s stale negative row is replaced, not just deleted

The existing `[InlineData("v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h")]` row (a four-segment string,
asserted to fail) is replaced with a five-segment row,
`"v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h:extra"`, so the theory keeps proving "a wrong number of
segments fails" — now at the boundary that is actually still wrong (five), rather than the one
this slice makes valid (four). Two new, separate tests cover the newly-valid four-segment shape
positively (Step 5): one for the `Encode` side, one for the `TryDecode` side, matching this
project's existing one-fact-per-behaviour granularity rather than folding a new claim into the
existing theory.

### 15. Documentation: spec §6.4 and §4.2, the backlog's F11 entry, and the `DeliveryAttempts` row — scoped precisely, not expanded

**In scope, corrected in this commit:** spec §6.4's button table and its `EditAction`-costs-an-LLM-
call sentence (both describe a shape this slice no longer builds); spec §4.2's
`RescheduleAsync`/`DeliveryAttempts` bullet (a forward-reference this slice both fulfils in part
and corrects); the backlog's F11 entry (renamed in spirit though not in its section heading,
corrected for the three-action split and `DeliveryAttempts`, and given a `*Settled at F11-1:*`
block per the pattern F10's own multi-slice entry already established); the backlog's
`DeliveryAttempts` deferred-property row (`F11` corrected to `F13`).

**Deliberately out of scope, considered and rejected.** Spec §3.4's folder-layout illustration and
§3.6's extension-seams table both still list `SnoozeAction`, `RescheduleAction`, `EditAction`
verbatim — and are left untouched. Both sit inside "Solution structure," a target-architecture
illustration for the whole of slice 1 that has never been kept in lock-step with what has actually
shipped: §3.4's own `Tools/` row still lists `ListTasksTool`, `UpdateTaskTool`, and
`CompleteTaskTool`, none of which exist yet (confirmed: `src/Assistant.Impl/Tools/` holds only
`CreateTaskTool.cs`), and nobody corrected that when F10 shipped only `CreateTaskTool`. Correcting
the Actions row alone while leaving the identically-stale Tools row would be an inconsistent,
arbitrary edit to a section this project's own history shows is not maintained incrementally. The
brief's own scope for this slice's documentation is precise — "spec 6.4's button table" and "the
backlog's F11 entry" — and this plan honours that precisely rather than reading it as licence to
sweep the whole document. **`docs/plans/2026-09-04-f6-2-action-catalogue.md`,
`docs/plans/2026-09-03-f6-2-route-the-tap.md`, `docs/plans/2026-09-04-f6-3-the-button-appears.md`,
and the two pre-YAGNI-reset `docs/2026-08-16-*.md` documents all mention `SnoozeAction`/
`RescheduleAction`/`EditAction` as forward-looking forecasts and are historical, already-shipped
plans — untouched, per the standing rule never to rewrite a plan that has already shipped.**

**The `observable` tag is added to the F11 backlog header now, not deferred to F11-2.** F10's own
entry carried `observable` from before any of its four sub-slices landed, and stayed unmet by that
tag until the sub-slice that actually delivered the real-phone-verifiable milestone (F10-3). F11-1
is explicitly framed, in this slice's own goal, as independently verifiable on a real phone — tap
`+1h`, see the message rewrite, see the reminder fire again an hour later — so the tag describes a
real property of this feature from its very first landed pull request, exactly mirroring F10's own
precedent, even though the entry as a whole stays open until F11-2's presets and submenu land.

**`docs/e2e-local.md` gains one paragraph beyond the brief's own explicit documentation scope — a
judgment call, flagged rather than silently added.** The brief's documentation-update item names
only spec §6.4 and the backlog; it does not mention `e2e-local.md`. This plan adds one paragraph to
the existing "Walkthrough against real Telegram" section anyway, because that section already
carries the exact real-phone verification story for Done (added at F6), and this slice's own goal
statement explicitly frames `+1h` as needing the identical proof. Leaving the walkthrough silent on
the one new user-facing affordance this slice ships would mean the file no longer describes the
full real-phone check the owner would actually want to run. This is called out explicitly in the
final report as a decision the operator should feel free to cut if unwanted.

---

## What this slice does NOT include

- **The `[Schedule]` button and its preset submenu.** F11-1's keyboard is `[Done] [+1h]`, flat, two
  buttons, one row. Tapping `+1h` performs the reschedule directly; nothing swaps a keyboard.
- **The `+3h`, `Tonight 20:00`, `Tomorrow 09:00` presets, and the `Back` button.** `ScheduleAction`
  understands exactly one argument value, `"+1h"`; any other value is refused (Decision 5).
- **Any `ILocalTimeResolver` use inside `ScheduleAction`.** See Decision 4 — this is a structural
  property of the type, not merely an unused-for-now dependency.
- **Keyboard variation in `CallbackRouter`** — choosing between a default keyboard and a
  preset-menu keyboard. The router builds no keyboard at all; `INotifier` does, and always the same
  one, for any non-completed task.
- **`DeliveryAttempts` returning to `ReminderTask`.** A delivery-retry concern, F13's, not this
  slice's — corrected in the backlog (Decision 15), not built here.
- **Any change to `docs/e2e-local.md`'s stub walkthrough, `AiClientTests.cs`,
  `CreateTaskToolTests.cs`, or any `Architecture`/`Convention` test.** Confirmed unaffected by
  reading each: none references `ITaskAction`, `ITaskService.CompleteAsync`, `INotifier`, or
  `TaskActions` in a way this slice's signature changes touch.
- **Storing the capture message's id, or a fired reminder deleting an earlier message.** That is
  F10-5, unscheduled into this feature.
- **Closing GitHub issue #27.** Mentioned as partially addressed (Decision 4, Decision 9); the
  issue stays open.
- **Any rewrite of spec §3.4, §3.6, or any already-shipped plan document.** See Decision 15.
- **A new project reference, a new NuGet package, or any `Assistant.Repository`/migration change.**
  Confirmed by "Verified facts."

---

## File Structure

```
src/Assistant.Contracts/
    ErrorCode.cs                                        + TaskActionArgumentUnrecognized
    TaskActions.cs                                       + Schedule, All now [Done, Schedule]

src/Assistant.Interfaces/
    ITaskAction.cs                                        ExecuteAsync gains `argument`, returns
                                                           Result<ReminderTask>
    ITaskService.cs                                       CompleteAsync returns Result<ReminderTask>,
                                                          + RescheduleAsync
    INotifier.cs                                         + UpdateTaskAsync

src/Assistant.Impl/
    Services/TaskService.cs                               CompleteAsync return type,
                                                          + RescheduleAsync
    Services/Actions/DoneAction.cs                        ExecuteAsync signature only
    Services/Actions/ScheduleAction.cs (new)              the +1h preset
    Telegram/CallbackCodec.cs                             TryDecode/Encode gain the 4th segment
    Telegram/CallbackRouter.cs                            renders by task Status, not action key
    Telegram/TelegramNotifier.cs                          two-button keyboard, + UpdateTaskAsync
    Telegram/MessageHandler.cs                            reply text extracted to the shared renderer
    Telegram/ReminderTaskTextExtensions.cs (new)          the shared ToMessageText renderer
    ImplServiceCollectionExtensions.cs                    + ScheduleAction registration

tests/Assistant.UnitTests/
    Telegram/CallbackCodecTests.cs                        4-segment coverage, stale row replaced

tests/Assistant.IntegrationTests/
    Services/TaskServiceTests.cs                          + 4 RescheduleAsync Facts
    Telegram/CallbackRouterTests.cs                       + FakeTimeProvider, + 3 Facts,
                                                           + 1 InlineData row + doc correction
    Telegram/TelegramListenerTests.cs                     2 existing Facts updated for 2 buttons
    Jobs/DueReminderJobTests.cs                            1 existing Fact updated for 2 buttons

docs/design/
    slice-1-reminders.md                                  §6.4 table + §4.2 bullet corrected
    2026-08-22-slice-1-feature-backlog.md                 F11 entry + DeliveryAttempts row corrected

docs/
    e2e-local.md                                          + +1h real-phone verification paragraph
```

No `Assistant.Models`, `Assistant.Repository`, `Assistant.Worker`, or migration file is touched.

---

## Validation

**Test count arithmetic.** Baseline, run directly (see "Verified facts"): 57 unit, 70 integration.

- Unit: `CallbackCodecTests.cs` gains 2 net new `[Fact]`s (one renamed, two added, one theory row
  swapped, not counted). 57 + 2 = **59**.
- Integration: `TaskServiceTests.cs` gains 4 `[Fact]`s. `CallbackRouterTests.cs` gains 3 `[Fact]`s
  and 1 `[InlineData]` row on an existing `[Theory]`. `DueReminderJobTests.cs` and
  `TelegramListenerTests.cs` each update existing tests with no count change. 70 + 4 + 3 + 1 =
  **78**.

**Expected final state: 59 unit, 78 integration — already confirmed, not merely predicted.** Both
isolated worktrees built during this planning session reached exactly this state:

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed!  - Failed: 0, Passed: 59, Skipped: 0, Total: 59 - Assistant.UnitTests.dll
Passed!  - Failed: 0, Passed: 78, Skipped: 0, Total: 78 - Assistant.IntegrationTests.dll
```

No `docker compose ... --build` is required — see Decision 12. If the `personal-ai-assistant-
postgres-test-1`/`personal-ai-assistant-wiremock-1` containers are not already running, `docker
compose -f compose.test.yaml up -d` (no `--build`) is sufficient.

---

## Steps

**Decisions this slice carries:** all fifteen, given in full above.

**Consumes:** `CallbackCodec`'s existing three-segment shape and `v1:` versioning (F6-2),
`TaskActionDefinition`'s record shape (F6-2), `CallbackRouter`'s owner-check and
answer-every-branch structure (F6-2, F6-3), `TelegramNotifier`'s `Escape`/`NoButtons` machinery
(F6-3), `MessageHandler`'s capture flow (F10), the owner's-message-deleted-on-capture behaviour
(F10-4).
**Produces:** `ScheduleAction`; `ITaskService.RescheduleAsync`; `INotifier.UpdateTaskAsync`; the
shared `ToMessageText` renderer; the codec's fourth segment; the router's status-based rendering;
the two-button keyboard; the doc corrections.

**Why this lands as one commit, not several.** `ITaskAction.ExecuteAsync`'s new parameter and
`ITaskService.CompleteAsync`'s new return type each have exactly one call site today
(`CallbackRouter.cs` and `DoneAction.cs` respectively — see "Verified facts"), but C# requires an
interface and every one of its implementations and call sites to agree on a signature in the same
compilation. Landing `CallbackCodec`'s new segment without `CallbackRouter`'s updated call to it,
or `ITaskAction`'s new shape without `DoneAction` and `CallbackRouter` following in the same
change, leaves the solution not compiling — there is no smaller independently-buildable unit
inside this interconnected core than all of it together, the same reasoning F10-4's own plan gave
for its own single commit. What genuinely can be built and proven independently (the two new,
purely additive catalogue/enum entries; the extraction of the shared renderer) is sequenced first,
below, each with its own real build-and-test checkpoint, before the interconnected core lands as
one step.

### Commit 1: the clock moves

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`
- Modify: `src/Assistant.Contracts/TaskActions.cs`
- Modify: `src/Assistant.Interfaces/ITaskAction.cs`
- Modify: `src/Assistant.Interfaces/ITaskService.cs`
- Modify: `src/Assistant.Interfaces/INotifier.cs`
- Modify: `src/Assistant.Impl/Services/TaskService.cs`
- Modify: `src/Assistant.Impl/Services/Actions/DoneAction.cs`
- Create: `src/Assistant.Impl/Services/Actions/ScheduleAction.cs`
- Modify: `src/Assistant.Impl/Telegram/CallbackCodec.cs`
- Modify: `src/Assistant.Impl/Telegram/CallbackRouter.cs`
- Modify: `src/Assistant.Impl/Telegram/TelegramNotifier.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Create: `src/Assistant.Impl/Telegram/ReminderTaskTextExtensions.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`
- Modify: `docs/design/slice-1-reminders.md`
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`
- Modify: `docs/e2e-local.md`

- [ ] **Step 1: Add the new `ErrorCode` member**

Purely additive — nothing reads it yet. In `src/Assistant.Contracts/ErrorCode.cs`, append after
`ModelNamedUnknownTool`:

```csharp

    /// <summary>
    /// A task action's callback argument was not one it understands.
    /// </summary>
    TaskActionArgumentUnrecognized,
}
```

(The `}` above is the enum's own closing brace, already present — this replaces it with the new
member inserted before it.)

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 2: Add the `Schedule` catalogue entry**

Replace `src/Assistant.Contracts/TaskActions.cs` in full:

```csharp
namespace Assistant.Contracts;

/// <summary>
/// Every action an inline button can perform on a task, in the one place both
/// <c>CallbackRouter</c> and a future button-rendering caller can read.
/// </summary>
/// <remarks>
/// <see cref="Done"/> and <see cref="Schedule"/> are declared before <see cref="All"/> because C#
/// runs a type's static member initializers in declaration order, and <see cref="All"/>'s own
/// initializer reads both of them -- declaring either one after <see cref="All"/> compiles cleanly
/// but leaves <see cref="All"/> holding a <see langword="null"/> element for whichever one had not
/// yet run.
/// </remarks>
public static class TaskActions
{
    /// <summary>
    /// The Done button's definition.
    /// </summary>
    public static TaskActionDefinition Done { get; } = new(
        Key: "done",
        Label: "Done",
        Description: "Marks the task complete. Refused when the task is already complete.");

    /// <summary>
    /// The schedule button's definition.
    /// </summary>
    /// <remarks>
    /// At F11-1 this is a single flat button, labelled with the one preset it understands rather
    /// than a menu-opening "Schedule" label -- there is no submenu yet for it to open. F11-2 adds
    /// the remaining presets and the menu this label will then front.
    /// </remarks>
    public static TaskActionDefinition Schedule { get; } = new(
        Key: "schedule",
        Label: "+1h",
        Description: "Moves the task's due time to one hour from now and clears its reminder-sent "
            + "marker so it fires again. Understands exactly one argument at F11-1, \"+1h\"; more "
            + "presets arrive at F11-2.");

    /// <summary>
    /// Every declared action, in declaration order.
    /// </summary>
    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done, Schedule];
}
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: build `0 Warning(s), 0 Error(s)`. Unit: **57 passed** — `TaskActionsTests`'s two existing
facts (`All_EveryDeclaredKey_IsUnique`, `All_EveryDeclaredKey_ContainsNoColon`) now exercise two
catalogue entries instead of one and still pass, with no code change to that test file. Integration:
**69 passed, 1 failed** — `CallbackRouterTests.ITaskAction_RegisteredImplementations_
MatchTheCatalogueKeysExactly` now fails, because the catalogue names two keys (`done`, `schedule`)
while the container still registers only `DoneAction`. This is expected and stays red through
Step 9; it closes at Step 20 once `ScheduleAction` is registered (see "Verified facts").

- [ ] **Step 3: Add the shared due-time renderer**

New file, purely additive — nothing calls it yet. Create
`src/Assistant.Impl/Telegram/ReminderTaskTextExtensions.cs`:

```csharp
using System.Globalization;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Renders the text a task's own Telegram message carries.
/// </summary>
/// <remarks>
/// Shared by <see cref="MessageHandler"/>, which builds it once when a capture first announces a
/// task, and <see cref="CallbackRouter"/>, which rebuilds it after an action changes the task
/// without completing it -- so a rescheduled task's message is rewritten to show its new due time
/// rather than being left showing the one it replaced. Extracted rather than duplicated: two
/// copies of the same interpolation would drift the first time either call site's wording changed
/// without the other noticing.
/// </remarks>
internal static class ReminderTaskTextExtensions
{
    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";

    /// <summary>
    /// Renders the sentence a task's own message shows.
    /// </summary>
    /// <param name="task">The task to render.</param>
    /// <param name="clock">Converts the task's stored UTC due instant back to local time.</param>
    /// <returns>
    /// <c>"{Title} -- due {local due time}."</c> when <see cref="ReminderTask.DueAt"/> is set, or
    /// <c>"{Title} -- saved with no reminder."</c> when it is <see langword="null"/>.
    /// </returns>
    public static string ToMessageText(this ReminderTask task, ILocalTimeResolver clock) =>
        task.DueAt is { } dueAt
            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
            : $"{task.Title} -- saved with no reminder.";
}
```

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Extract `MessageHandler`'s reply text to the shared renderer**

In `src/Assistant.Impl/Telegram/MessageHandler.cs`, remove the now-unused import and constant:

```diff
-using System.Globalization;
 using Assistant.Contracts;
```

```diff
     : ITelegramUpdateHandler
 {
-    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";
-
     private const string Unreachable =
```

and replace the reply construction:

```diff
         var task = outcome.Value!;
-        var reply = task.DueAt is { } dueAt
-            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
-            : $"{task.Title} -- saved with no reminder.";
-
-        await notifier.SendTaskAsync(task.Id, reply, ct);
+        await notifier.SendTaskAsync(task.Id, task.ToMessageText(clock), ct);
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: build `0 Warning(s), 0 Error(s)`. Unit: **57 passed**. Integration: **69 passed, 1
failed** (the same expected `ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly`
failure from Step 2 — nothing here touches it). The two `Listener_OwnerSendsAMessageWith*` tests in
`TelegramListenerTests.cs` still pass unchanged: the rendered text is byte-identical to before this
extraction, confirmed directly by this run.

- [ ] **Step 5: Write every test the interconnected core needs — watch the whole solution fail to compile**

Five files change together; none of them compiles against today's `CallbackCodec`/`ITaskAction`/
`ITaskService` shapes, which is the point of this step.

Replace `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs` in full:

```csharp
using Assistant.Impl.Telegram;

namespace Assistant.UnitTests.Telegram;

/// <summary>
/// Test class for <see cref="CallbackCodec"/>.
/// </summary>
public sealed class CallbackCodecTests
{
    /// <summary>
    /// When a known task id is encoded with no argument
    /// Then the exact three-segment wire string is produced.
    /// </summary>
    [Fact]
    public void Encode_KnownTaskId_ProducesTheExpectedString()
    {
        // Act
        var data = CallbackCodec.Encode("done", Guid.Empty);

        // Assert
        Assert.Equal("v1:done:AAAAAAAAAAAAAAAAAAAAAA==", data);
    }

    /// <summary>
    /// When a known task id and argument are encoded
    /// Then the exact four-segment wire string is produced.
    /// </summary>
    [Fact]
    public void Encode_KnownTaskIdAndArgument_ProducesTheExpectedString()
    {
        // Act
        var data = CallbackCodec.Encode("schedule", Guid.Empty, "+1h");

        // Assert
        Assert.Equal("v1:schedule:AAAAAAAAAAAAAAAAAAAAAA==:+1h", data);
    }

    /// <summary>
    /// When a string is encoded for a task with no argument
    /// And that same string is decoded
    /// Then the original action and task id are recovered
    /// And the argument is empty.
    /// </summary>
    [Fact]
    public void TryDecode_WellFormedThreeSegmentString_RecoversTheActionAndTaskIdWithAnEmptyArgument()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var data = CallbackCodec.Encode("done", taskId);

        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var recoveredId, out var argument);

        // Assert
        Assert.True(decoded);
        Assert.Equal("done", action);
        Assert.Equal(taskId, recoveredId);
        Assert.Equal(string.Empty, argument);
    }

    /// <summary>
    /// When a string is encoded for a task with an argument
    /// And that same string is decoded
    /// Then the original action, task id and argument are all recovered.
    /// </summary>
    [Fact]
    public void TryDecode_WellFormedFourSegmentString_RecoversTheActionTaskIdAndArgument()
    {
        // Arrange
        var taskId = Guid.NewGuid();
        var data = CallbackCodec.Encode("schedule", taskId, "+1h");

        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var recoveredId, out var argument);

        // Assert
        Assert.True(decoded);
        Assert.Equal("schedule", action);
        Assert.Equal(taskId, recoveredId);
        Assert.Equal("+1h", argument);
    }

    /// <summary>
    /// When a string does not match the v1:&lt;action&gt;:&lt;base64-id&gt;[:&lt;arg&gt;] shape
    /// Then it is not decoded.
    /// </summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("v1:done")]
    [InlineData("v2:done:AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("v1:done:not-valid-base64!!")]
    [InlineData("v1:done:AAAA")]
    [InlineData("v1:done:AAAAAAAAAAAAAAAAAAAAAA==:1h:extra")]
    public void TryDecode_MalformedOrUnsupportedStrings_Fails(string data)
    {
        // Act
        var decoded = CallbackCodec.TryDecode(data, out var action, out var taskId, out var argument);

        // Assert
        Assert.False(decoded);
        Assert.Equal(string.Empty, action);
        Assert.Equal(Guid.Empty, taskId);
        Assert.Equal(string.Empty, argument);
    }
}
```

In `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`, insert before the
`CreateAsync_TitleAndResolvedDueTime_StoresAPendingTaskAndReturnsIt` test:

```csharp

    /// <summary>
    /// When a task whose reminder already fired is rescheduled
    /// Then its due time becomes the given instant
    /// And its reminder-sent marker is cleared so it fires again.
    /// </summary>
    [Fact]
    public async Task RescheduleAsync_ReminderAlreadySent_SetsTheNewDueTimeAndClearsReminderSentAt()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: AsOf.AddHours(-1), reminderSentAt: AsOf.AddHours(-1));
        await postgres.SaveAsync(reminderTask);
        var newDueAt = AsOf.AddHours(1);

        // Act
        var result = await _sut.RescheduleAsync(reminderTask.Id, newDueAt, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var stored = await _repository.FindAsync(reminderTask.Id, CancellationToken.None);
        Assert.Equal(newDueAt, stored!.DueAt);
        Assert.Null(stored.ReminderSentAt);
    }

    /// <summary>
    /// When a task with no due time is rescheduled
    /// Then it gains the given due time, for the first time.
    /// </summary>
    [Fact]
    public async Task RescheduleAsync_TaskHadNoDueTime_GivesItADueTimeForTheFirstTime()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: null);
        await postgres.SaveAsync(reminderTask);
        var newDueAt = AsOf.AddHours(1);

        // Act
        var result = await _sut.RescheduleAsync(reminderTask.Id, newDueAt, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        var stored = await _repository.FindAsync(reminderTask.Id, CancellationToken.None);
        Assert.Equal(newDueAt, stored!.DueAt);
    }

    /// <summary>
    /// When a task is already completed
    /// And it is rescheduled
    /// Then it is refused as already completed
    /// And its due time is unchanged.
    /// </summary>
    [Fact]
    public async Task RescheduleAsync_TaskAlreadyCompleted_IsRejectedAndDueTimeUnchanged()
    {
        // Arrange
        var originalDueAt = AsOf.AddHours(-3);
        var reminderTask = BuildReminderTask(
            dueAt: originalDueAt, status: ReminderStatus.Completed, completedAt: originalDueAt);
        await postgres.SaveAsync(reminderTask);

        // Act
        var result = await _sut.RescheduleAsync(reminderTask.Id, AsOf.AddHours(1), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TaskAlreadyCompleted, result.Error);
        var stored = await _repository.FindAsync(reminderTask.Id, CancellationToken.None);
        Assert.Equal(originalDueAt, stored!.DueAt);
    }

    /// <summary>
    /// When no task carries the requested identifier
    /// And it is rescheduled
    /// Then it is refused rather than silently doing nothing.
    /// </summary>
    [Fact]
    public async Task RescheduleAsync_TaskDoesNotExist_IsRejected()
    {
        // Act
        var result = await _sut.RescheduleAsync(Guid.NewGuid(), AsOf.AddHours(1), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TaskNotFound, result.Error);
    }
```

In `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`, add the `FakeTimeProvider`
import and registration:

```diff
 using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.Hosting;
+using Microsoft.Extensions.Time.Testing;
 using static Assistant.IntegrationTests.Infrastructure.ReminderTaskBuilder;
```

```diff
         services.AddAssistantServices();
+        services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));
         services.AddAssistantTelegram(new TelegramSettings
```

Update the unrecognised-callback theory's doc comment and add one row:

```diff
     /// <summary>
-    /// When the callback data is malformed or names an action nothing implements
+    /// When the callback data is malformed, names an action nothing implements, or carries an
+    /// argument the named action does not understand
     /// Then the callback query is still answered
     /// And nothing is edited.
     /// </summary>
     [Theory]
     [InlineData("garbage")]
     [InlineData("v1:archive:AAAAAAAAAAAAAAAAAAAAAA==")]
+    [InlineData("v1:schedule:AAAAAAAAAAAAAAAAAAAAAA==:+3h")]
     public async Task Listener_UnrecognisedCallbackData_StillAnswersButEditsNothing(string data)
```

Insert before `ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly`:

```csharp

    /// <summary>
    /// When the owner taps +1h on a task whose reminder already fired
    /// Then the task's due time moves to one hour from now
    /// And its reminder-sent marker is cleared so it fires again
    /// And the message is rewritten with the new due time
    /// And it keeps the Done and Schedule buttons
    /// And the callback query is answered with no toast.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerTapsPlusOneHour_ReschedulesAndRewritesTheMessageWithTheNewDueTime()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: AsOf.AddHours(-1), reminderSentAt: AsOf.AddHours(-1));
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h");
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, null);
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);

        var expectedKeyboard = new ReplyMarkupPayload(
        [
            [
                new InlineButtonPayload(TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, task.Id)),
                new InlineButtonPayload(
                    TaskActions.Schedule.Label, CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h")),
            ],
        ]);
        var expectedEdit = new EditMessageTextPayload(
            OwnerChatId, MessageId, "call the bank -- due Tuesday 25 August 2026, 16:00.", "Html", expectedKeyboard);
        Assert.Equivalent(expectedEdit, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(AsOf.AddHours(1), stored!.DueAt);
        Assert.Null(stored.ReminderSentAt);
    }

    /// <summary>
    /// When the owner taps +1h on a task that has never had a due time
    /// Then the task gains a due time for the first time, one hour from now.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerTapsPlusOneHourOnAnUndatedTask_GivesItADueTimeForTheFirstTime()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: null);
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h");
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(AsOf.AddHours(1), stored!.DueAt);
    }

    /// <summary>
    /// When the owner taps +1h on a task that is already completed
    /// Then the callback query is answered that it is already done
    /// And the task's due time is left unchanged
    /// And nothing is edited.
    /// </summary>
    [Fact]
    public async Task Listener_PlusOneHourTappedOnAnAlreadyCompletedTask_AnswersAlreadyDoneWithoutRescheduling()
    {
        // Arrange
        var originalDueAt = AsOf.AddHours(-3);
        var task = BuildReminderTask(
            dueAt: originalDueAt, status: ReminderStatus.Completed, completedAt: originalDueAt);
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h");
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, "Already done.");
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);

        Assert.Empty(await wireMock.EditedMessagesAsync());

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(originalDueAt, stored!.DueAt);
    }
```

In `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`, update the two capture-
reply tests and the one `<see cref>` pointing at the renamed one:

```diff
     /// <summary>
     /// When the owner sends a message
     /// And the model calls create_task with a due time that resolves
     /// Then the task is stored with that due instant
     /// And the owner is told the title and the due time, rendered in the configured zone
-    /// And the reply carries a Done button for that exact task.
+    /// And the reply carries the Done and Schedule buttons for that exact task.
     /// </summary>
     [Fact]
-    public async Task Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton()
+    public async Task Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndBothButtons()
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

-        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
-        var button = Assert.Single(row);
-        Assert.Equal(TaskActions.Done.Label, button.Text);
-        Assert.Equal(CallbackCodec.Encode(TaskActions.Done.Key, stored.Id), button.CallbackData);
+        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
+        Assert.Equal([TaskActions.Done.Label, TaskActions.Schedule.Label], row.Select(button => button.Text));
+        Assert.Equal(
+            [CallbackCodec.Encode(TaskActions.Done.Key, stored.Id),
+                CallbackCodec.Encode(TaskActions.Schedule.Key, stored.Id, "+1h")],
+            row.Select(button => button.CallbackData));
     }

     /// <summary>
     /// When the owner sends a message
     /// And the model calls create_task with no due time
     /// Then the owner is told plainly that no reminder will fire
-    /// And the reply still carries a Done button.
+    /// And the reply still carries the Done and Schedule buttons.
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

-        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
-        var button = Assert.Single(row);
-        Assert.Equal(TaskActions.Done.Label, button.Text);
+        var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
+        Assert.Equal([TaskActions.Done.Label, TaskActions.Schedule.Label], row.Select(button => button.Text));
     }
```

and, further down, the one `<see cref>` naming the renamed test:

```diff
-    /// <see cref="Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton"/>,
+    /// <see cref="Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndBothButtons"/>,
```

In `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`:

```diff
     /// <summary>
     /// When a task is due
     /// And the job runs
-    /// Then the reminder carries exactly one button
-    /// And that button's callback data decodes to the same task
-    /// And its label is the catalogue's Done label.
+    /// Then the reminder carries exactly one row of two buttons
+    /// And their callback data both decode to the same task
+    /// And their labels are the catalogue's Done and Schedule labels, in that order.
     /// </summary>
     [Fact]
-    public async Task RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask()
+    public async Task RunAsync_TaskIsDue_AttachesTheDoneAndScheduleButtonsForThatTask()
     {
         // Arrange
         var task = BuildReminderTask(dueAt: DateTimeOffset.UtcNow.AddHours(-1));
         await postgres.SaveAsync(task);

         // Act
         await _sut.RunAsync(CancellationToken.None);

         // Assert
         var sent = Assert.Single(await wireMock.SentMessagesAsync());
-        var row = Assert.Single(sent.ReplyMarkup!.InlineKeyboard);
-        var button = Assert.Single(row);
-        Assert.Equal(TaskActions.Done.Label, button.Text);
-        Assert.Equal(CallbackCodec.Encode(TaskActions.Done.Key, task.Id), button.CallbackData);
+        var row = Assert.Single(sent.ReplyMarkup!.InlineKeyboard);
+        Assert.Equal(
+            [TaskActions.Done.Label, TaskActions.Schedule.Label],
+            row.Select(button => button.Text));
+        Assert.Equal(
+            [CallbackCodec.Encode(TaskActions.Done.Key, task.Id),
+                CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h")],
+            row.Select(button => button.CallbackData));
     }
```

```bash
dotnet build --no-restore
```

Expected: the build **fails**, with exactly this shape of error, across both test projects (the
exact text below is what this planning session's own isolated worktree produced from this precise
state — captured, not composed):

```
.../tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs(32,34): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs(52,37): error CS1501: No overload for method 'TryDecode' takes 4 arguments
.../tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs(71,34): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs(74,37): error CS1501: No overload for method 'TryDecode' takes 4 arguments
.../tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs(97,37): error CS1501: No overload for method 'TryDecode' takes 4 arguments
.../tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs(95,31): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs(171,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
.../tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs(193,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
.../tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs(217,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
.../tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs(234,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
.../tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs(261,34): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs(278,63): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs(300,34): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs(327,34): error CS1501: No overload for method 'Encode' takes 3 arguments
.../tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs(119,31): error CS1501: No overload for method 'Encode' takes 3 arguments

15 Error(s)
```

This is the RED this slice's own core is measured against: every one of these fifteen errors names
either `CallbackCodec`'s missing three-argument `Encode`/four-`out`-parameter `TryDecode`, or
`ITaskService`'s missing `RescheduleAsync` — exactly the two surfaces Step 6 adds. (The
`TaskActions.Schedule`-related errors this same test set would otherwise also produce are already
absent, because Step 2 landed that catalogue entry first.)

- [ ] **Step 6: Implement the interconnected core — codec, interfaces, actions, router, notifier, DI**

Replace `src/Assistant.Impl/Telegram/CallbackCodec.cs` in full:

```csharp
namespace Assistant.Impl.Telegram;

/// <summary>
/// Encodes and decodes the <c>callback_data</c> string carried on an inline button.
/// </summary>
/// <remarks>
/// The wire format is <c>v1:&lt;action&gt;:&lt;base64-id&gt;[:&lt;arg&gt;]</c>, per spec 6.4. The
/// version prefix means a button left in chat history from a build that no longer understands its
/// exact format degrades to a polite reply instead of throwing. <see cref="TryDecode"/> accepts
/// either the three-segment form every button before F11-1 ever produced, or the four-segment
/// form <see cref="Assistant.Impl.Services.Actions.ScheduleAction"/> is the first action to need
/// -- a three-segment string yields an empty argument, so every button already sitting in the
/// owner's chat history keeps working unchanged.
/// </remarks>
internal static class CallbackCodec
{
    private const string Prefix = "v1";

    /// <summary>
    /// Builds the callback data string for a button with no argument.
    /// </summary>
    /// <param name="action">The action's key, matching <c>ITaskAction.Key</c>.</param>
    /// <param name="taskId">The task the button refers to.</param>
    /// <returns>
    /// A string of the form <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c> -- 32 characters for the
    /// four-letter key <c>done</c>, comfortably inside Telegram's 64-byte callback data limit.
    /// </returns>
    public static string Encode(string action, Guid taskId) =>
        $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}";

    /// <summary>
    /// Builds the callback data string for a button carrying an argument.
    /// </summary>
    /// <param name="action">The action's key, matching <c>ITaskAction.Key</c>.</param>
    /// <param name="taskId">The task the button refers to.</param>
    /// <param name="argument">The argument the action reads back from <see cref="TryDecode"/>.</param>
    /// <returns>
    /// A string of the form <c>v1:&lt;action&gt;:&lt;base64-id&gt;:&lt;arg&gt;</c> -- 37 characters
    /// for the key <c>schedule</c> and the argument <c>+1h</c>, comfortably inside Telegram's
    /// 64-byte callback data limit.
    /// </returns>
    public static string Encode(string action, Guid taskId, string argument) =>
        $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}:{argument}";

    /// <summary>
    /// Attempts to decode a callback data string.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <param name="argument">
    /// The decoded fourth segment, or an empty string when <paramref name="data"/> carried only
    /// three segments or decoding failed altogether.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="data"/> is a well-formed
    /// <c>v1:&lt;action&gt;:&lt;base64-id&gt;</c> or <c>v1:&lt;action&gt;:&lt;base64-id&gt;:&lt;arg&gt;</c>
    /// string; <see langword="false"/> for anything else, including a different version prefix, a
    /// wrong number of segments, or an id segment that is not valid base64 encoding exactly 16
    /// bytes.
    /// </returns>
    public static bool TryDecode(string data, out string action, out Guid taskId, out string argument)
    {
        action = string.Empty;
        taskId = Guid.Empty;
        argument = string.Empty;

        var parts = data.Split(':');

        if ((parts.Length != 3 && parts.Length != 4) || parts[0] != Prefix)
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (bytes.Length != 16)
        {
            return false;
        }

        action = parts[1];
        taskId = new Guid(bytes);
        argument = parts.Length == 4 ? parts[3] : string.Empty;
        return true;
    }
}
```

Replace `src/Assistant.Interfaces/ITaskAction.cs` in full:

```csharp
using Assistant.Contracts;
using Assistant.Models;

namespace Assistant.Interfaces;

/// <summary>
/// One action an inline button's tap can perform on a task.
/// </summary>
/// <remarks>
/// Resolved by matching <see cref="Definition"/>'s <see cref="TaskActionDefinition.Key"/> against
/// the callback codec's decoded action segment. A caller that finds no implementation whose key
/// matches produces a polite reply rather than throwing, per spec 6.4. <c>DoneAction</c> is the
/// first implementation and <c>ScheduleAction</c> (F11-1) the second, resolved the same way. This
/// interface was modified in place to add <c>argument</c> below rather than gaining a second,
/// argument-taking sibling interface for <c>ScheduleAction</c> alone -- the same posture
/// <see cref="ITaskService"/> and <see cref="INotifier"/> already take of growing an existing
/// interface rather than speculating a parallel one for each new capability.
/// </remarks>
public interface ITaskAction
{
    /// <summary>
    /// This action's entry in the shared catalogue.
    /// </summary>
    /// <value>Key, label and description all come from <see cref="TaskActions"/>.</value>
    TaskActionDefinition Definition { get; }

    /// <summary>
    /// Performs the action against the given task.
    /// </summary>
    /// <param name="taskId">The task the button referred to.</param>
    /// <param name="argument">
    /// The callback data's optional fourth segment, or an empty string when the button carried
    /// none. An action that takes no argument ignores this; <c>ScheduleAction</c> is the first to
    /// read it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The task as it stands once the action has run, or the reason it was refused. Returning the
    /// task, rather than a bare success, is what lets a caller render the right thing afterwards
    /// without a second read: which rendering is right depends on the task's own resulting state,
    /// not on which action produced it.
    /// </returns>
    Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct);
}
```

In `src/Assistant.Interfaces/ITaskService.cs`, change `CompleteAsync`'s return type and add
`RescheduleAsync`:

```diff
     /// <remarks>
     /// A second call on an already-completed task is refused with
     /// <see cref="ErrorCode.TaskAlreadyCompleted"/> rather than repeating the write: the row is
     /// left exactly as the first call set it, so <see cref="ReminderTask.CompletedAt"/> always
-    /// carries the instant of the first completion, never a later one.
+    /// carries the instant of the first completion, never a later one. Returns the completed task
+    /// so a caller such as <c>CallbackRouter</c> can render from it directly.
     /// </remarks>
-    Task<Result> CompleteAsync(Guid id, CancellationToken ct);
+    Task<Result<ReminderTask>> CompleteAsync(Guid id, CancellationToken ct);
+
+    /// <summary>
+    /// Moves a task's due time to <paramref name="dueAtUtc"/> and re-arms its reminder.
+    /// </summary>
+    /// <param name="id">The task to reschedule.</param>
+    /// <param name="dueAtUtc">
+    /// The new due instant, in UTC. Resolving a relative preset (such as "one hour from now") or
+    /// an absolute local time to this instant is the caller's job -- this method only ever stores
+    /// the instant it is given.
+    /// </param>
+    /// <param name="ct">Cancellation token.</param>
+    /// <returns>
+    /// The rescheduled task, or the reason it was refused. Refused when no task carries the
+    /// identifier, or when the task has already been completed.
+    /// </returns>
+    /// <remarks>
+    /// Snooze and reschedule are the same operation: both set <see cref="ReminderTask.DueAt"/> and
+    /// clear <see cref="ReminderTask.ReminderSentAt"/> to <see langword="null"/>, so the task fires
+    /// again at its new due time. A completed task is refused with
+    /// <see cref="ErrorCode.TaskAlreadyCompleted"/>, the same rule and the same code
+    /// <see cref="CompleteAsync"/> already uses for a second completion -- a completed task's own
+    /// message carries no buttons, so this is reachable only from a stale button left in chat
+    /// history, and treating it as "already settled" rather than silently reopening the task is
+    /// the safer reading of a tap nobody could have meant to land here.
+    /// </remarks>
+    Task<Result<ReminderTask>> RescheduleAsync(Guid id, DateTimeOffset dueAtUtc, CancellationToken ct);
```

In `src/Assistant.Interfaces/INotifier.cs`, append `UpdateTaskAsync` after `MarkCompletedTaskAsync`:

```csharp

    /// <summary>
    /// Updates a previously sent task message to reflect a change other than completion, keeping
    /// its task keyboard attached.
    /// </summary>
    /// <param name="messageId">Identifier of the message to edit.</param>
    /// <param name="taskId">
    /// The task the message announces. The adapter needs this to rebuild the same channel-neutral
    /// handle for each action it re-attaches -- it never sees any other part of a database shape.
    /// </param>
    /// <param name="text">
    /// The message body, as plain text. The adapter escapes whatever its channel requires before
    /// sending, so callers must not pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the edit has been accepted.</returns>
    /// <remarks>
    /// Unlike <see cref="MarkCompletedTaskAsync"/>, this re-attaches the same keyboard
    /// <see cref="SendTaskAsync"/> would build for <paramref name="taskId"/>, rather than clearing
    /// it -- the task announced by <paramref name="messageId"/> is still open, so its actions must
    /// stay reachable from the message that announces it.
    /// </remarks>
    Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct);
```

In `src/Assistant.Impl/Services/TaskService.cs`, change `CompleteAsync` and add `RescheduleAsync`:

```diff
     /// <inheritdoc/>
-    public async Task<Result> CompleteAsync(Guid id, CancellationToken ct)
+    public async Task<Result<ReminderTask>> CompleteAsync(Guid id, CancellationToken ct)
     {
         var task = await repository.FindAsync(id, ct);

         if (task is null)
         {
-            return Result.Failure(ErrorCode.TaskNotFound);
+            return Result<ReminderTask>.Failure(ErrorCode.TaskNotFound);
         }

         if (task.Status == ReminderStatus.Completed)
         {
-            return Result.Failure(ErrorCode.TaskAlreadyCompleted);
+            return Result<ReminderTask>.Failure(ErrorCode.TaskAlreadyCompleted);
         }

         var now = timeProvider.GetUtcNow();

         task.Status = ReminderStatus.Completed;
         task.CompletedAt = now;
         task.UpdatedAt = now;
         await repository.UpdateAsync(task, ct);

-        return Result.Success();
+        return Result<ReminderTask>.Success(task);
+    }
+
+    /// <inheritdoc/>
+    public async Task<Result<ReminderTask>> RescheduleAsync(
+        Guid id, DateTimeOffset dueAtUtc, CancellationToken ct)
+    {
+        var task = await repository.FindAsync(id, ct);
+
+        if (task is null)
+        {
+            return Result<ReminderTask>.Failure(ErrorCode.TaskNotFound);
+        }
+
+        if (task.Status == ReminderStatus.Completed)
+        {
+            return Result<ReminderTask>.Failure(ErrorCode.TaskAlreadyCompleted);
+        }
+
+        task.DueAt = dueAtUtc;
+        task.ReminderSentAt = null;
+        task.UpdatedAt = timeProvider.GetUtcNow();
+        await repository.UpdateAsync(task, ct);
+
+        return Result<ReminderTask>.Success(task);
     }
```

Replace `src/Assistant.Impl/Services/Actions/DoneAction.cs` in full:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Services.Actions;

/// <summary>
/// Completes a task in response to its Done button being tapped.
/// </summary>
/// <param name="taskService">The single writer for tasks.</param>
internal sealed class DoneAction(ITaskService taskService) : ITaskAction
{
    /// <inheritdoc/>
    public TaskActionDefinition Definition => TaskActions.Done;

    /// <inheritdoc/>
    /// <remarks>
    /// Done takes no argument, so <paramref name="argument"/> is ignored.
    /// </remarks>
    public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
        taskService.CompleteAsync(taskId, ct);
}
```

Create `src/Assistant.Impl/Services/Actions/ScheduleAction.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Services.Actions;

/// <summary>
/// Moves a task's due time forward in response to its schedule button being tapped.
/// </summary>
/// <param name="taskService">The single writer for tasks.</param>
/// <param name="timeProvider">The current instant, added to for the "+1h" preset.</param>
/// <remarks>
/// Understands exactly one argument at F11-1, <c>"+1h"</c> -- the remaining presets spec 6.4
/// describes (<c>+3h</c>, <c>Tonight 20:00</c>, <c>Tomorrow 09:00</c>) arrive at F11-2, each
/// needing no change to <see cref="ITaskAction"/> itself, since <see cref="ExecuteAsync"/> already
/// carries the argument a new branch here would read.
/// <para>
/// The preset is added to <paramref name="timeProvider"/>'s current UTC instant, never to a local
/// wall-clock reading -- adding an hour to a wall-clock reading gives the wrong answer across a
/// daylight-saving boundary, where an hour of wall-clock time is not always an hour of elapsed
/// real time. This type takes no <see cref="ILocalTimeResolver"/> dependency at all, so there is
/// no local reading anywhere in scope to make that mistake with; <see cref="DateTimeOffset.AddHours"/>
/// on an instant is correct across a boundary by construction, not by care taken here.
/// </para>
/// <para>
/// The preset is measured from now, not from the task's own current due time: the owner taps this
/// because they are busy right now, and a reminder whose due time has already passed -- the usual
/// case for a tap made in response to a reminder that just fired -- would fire again immediately
/// if the preset were added to a due time already in the past instead of to now.
/// </para>
/// </remarks>
internal sealed class ScheduleAction(ITaskService taskService, TimeProvider timeProvider) : ITaskAction
{
    private const string OneHour = "+1h";

    /// <inheritdoc/>
    public TaskActionDefinition Definition => TaskActions.Schedule;

    /// <inheritdoc/>
    public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
        argument == OneHour
            ? taskService.RescheduleAsync(taskId, timeProvider.GetUtcNow().AddHours(1), ct)
            : Task.FromResult(Result<ReminderTask>.Failure(ErrorCode.TaskActionArgumentUnrecognized));
}
```

Replace `src/Assistant.Impl/Telegram/CallbackRouter.cs` in full:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Settings;
using Assistant.Interfaces;
using Assistant.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Assistant.Impl.Telegram;

/// <summary>
/// Routes an inline button's tap to the <see cref="ITaskAction"/> its callback data names, then
/// always answers the callback query.
/// </summary>
/// <param name="settings">Validated Telegram configuration, which carries the owner's chat.</param>
/// <param name="bot">The Telegram client, already pointed at a base address.</param>
/// <param name="notifier">Where a successful action's re-render is delivered.</param>
/// <param name="clock">Renders a rescheduled task's new due instant back in the configured local zone.</param>
/// <param name="actions">
/// Every registered task action, resolved by matching <see cref="TaskActionDefinition.Key"/>
/// against each one's <see cref="ITaskAction.Definition"/>.
/// </param>
/// <remarks>
/// The callback query is answered last in every branch, after any edit a successful action
/// triggers, never before -- every reachable path through <see cref="HandleAsync"/> ends with
/// exactly one call to <see cref="ITelegramBotClient"/>'s answer method and nothing after it, so
/// observing that one call is enough to know the whole update has been fully handled. The sole
/// exception is the first guard's bare early return, which answers nothing because there is no
/// callback query to answer at all -- and that branch is unreachable in practice, since
/// <see cref="TelegramListener.DispatchAsync"/> only invokes handlers whose <see cref="Handles"/>
/// matches the update's own type, and this handler declares <see cref="UpdateType.CallbackQuery"/>.
/// <para>
/// <c>Message.Text</c> is bound with a plain <c>var</c>, not a null-checked pattern, because
/// Telegram omits a message's text once it judges the message too old to still carry content --
/// exactly the age an old reminder's Done button can reach in chat history. The action still
/// runs and the query is still answered in that case; only a completed task's strike-through edit
/// is skipped, since there is no text left to strike through. A re-render for any other outcome
/// needs no previous text at all -- it is built fresh from the task -- so it is attempted either
/// way.
/// </para>
/// <para>
/// The owner check lives inline here, the same as <see cref="MessageHandler"/>'s own remarks
/// explain: nothing in <see cref="ITelegramUpdateHandler"/> or <see cref="TelegramListener"/>
/// enforces it. Unlike <see cref="MessageHandler"/>, a non-owner's tap is still answered -- spec
/// 6.4 requires every callback query to be answered, owner or not, or Telegram leaves that
/// tapper's own client spinning -- but the action itself never runs and nothing is edited.
/// </para>
/// <para>
/// Rendering is chosen from the task <see cref="ITaskAction.ExecuteAsync"/> hands back, by its
/// <see cref="ReminderTask.Status"/> -- never by which action ran, and never by a switch on
/// <see cref="TaskActionDefinition.Key"/>. A <see cref="ReminderStatus.Completed"/> task is struck
/// through and loses its keyboard, exactly as <see cref="TaskActions.Done"/> always rendered; any
/// other status is re-rendered in place with its task keyboard still attached, its text rebuilt
/// fresh from the task so a changed due time is never left showing a stale one. A future action
/// therefore needs no change here at all: it inherits whichever rendering its own resulting status
/// implies, the same registration-seam, Open/Closed posture <see cref="MessageHandler"/> already
/// gives tool dispatch.
/// </para>
/// </remarks>
internal sealed class CallbackRouter(
    TelegramSettings settings,
    ITelegramBotClient bot,
    INotifier notifier,
    ILocalTimeResolver clock,
    IEnumerable<ITaskAction> actions) : ITelegramUpdateHandler
{
    private const string ThatButtonIsNoLongerValid = "That button is no longer valid.";

    private const string AlreadyDone = "Already done.";

    private const string CouldNotFindThatTask = "I could not find that task.";

    /// <inheritdoc/>
    public UpdateType Handles => UpdateType.CallbackQuery;

    /// <inheritdoc/>
    public async Task HandleAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is not { } callbackQuery)
        {
            return;
        }

        if (callbackQuery is not
            {
                Id: var callbackQueryId,
                Data: { } data,
                Message: { Chat.Id: var chatId, Id: var messageId, Text: var messageText },
            })
        {
            await bot.AnswerCallbackQuery(callbackQuery.Id, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        if (chatId != settings.OwnerChatId)
        {
            await bot.AnswerCallbackQuery(callbackQueryId, cancellationToken: ct);
            return;
        }

        if (!CallbackCodec.TryDecode(data, out var actionKey, out var taskId, out var argument))
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var action = actions.FirstOrDefault(a => a.Definition.Key == actionKey);

        if (action is null)
        {
            await bot.AnswerCallbackQuery(callbackQueryId, ThatButtonIsNoLongerValid, cancellationToken: ct);
            return;
        }

        var result = await action.ExecuteAsync(taskId, argument, ct);

        if (result.IsSuccess)
        {
            var task = result.Value!;

            if (task.Status == ReminderStatus.Completed)
            {
                if (messageText is not null)
                {
                    await notifier.MarkCompletedTaskAsync(messageId, messageText, ct);
                }
            }
            else
            {
                await notifier.UpdateTaskAsync(messageId, task.Id, task.ToMessageText(clock), ct);
            }
        }

        var reply = result switch
        {
            { IsSuccess: true } => null,
            { Error: ErrorCode.TaskAlreadyCompleted } => AlreadyDone,
            { Error: ErrorCode.TaskActionArgumentUnrecognized } => ThatButtonIsNoLongerValid,
            _ => CouldNotFindThatTask,
        };

        await bot.AnswerCallbackQuery(callbackQueryId, reply, cancellationToken: ct);
    }
}
```

In `src/Assistant.Impl/Telegram/TelegramNotifier.cs`, replace `SendTaskAsync` and
`MarkCompletedTaskAsync`'s `<remarks>`, and add `UpdateTaskAsync` and `BuildTaskKeyboard`:

```diff
     /// <inheritdoc/>
     /// <remarks>
-    /// Builds a single button, the catalogue's <c>Done</c> entry, directly rather than by
-    /// iterating <c>TaskActions.All</c> -- <c>All</c> has exactly one entry, and a loop is
-    /// machinery for a plurality that does not exist. The
-    /// <see cref="InlineKeyboardMarkup(IEnumerable{InlineKeyboardButton})"/> overload an iteration
-    /// would need binds to the same row-wrapping constructor described in the
-    /// <see cref="NoButtons"/> comment above, so iterating would silently fix the layout at
-    /// "everything in one row" -- a decision that belongs to F11, which must also decide which
-    /// actions a given reminder shows. The button's callback data is
-    /// <c>CallbackCodec.Encode</c> applied to <c>Done.Key</c> and <paramref name="taskId"/>, the
-    /// same encoding <c>CallbackRouter</c> decodes on a tap. Its label is sent as-is:
-    /// <c>parse_mode</c> governs the message body, not a button's text, which Telegram carries as
-    /// a plain JSON string rather than parsed markup -- so a future label containing "&amp;" or
-    /// "&lt;" would still need no escaping here.
+    /// Attaches both catalogue buttons -- see <see cref="BuildTaskKeyboard"/> for how they are laid
+    /// out.
     /// </remarks>
-    public async Task SendTaskAsync(Guid taskId, string text, CancellationToken ct)
-    {
-        var keyboard = new InlineKeyboardMarkup(
-            InlineKeyboardButton.WithCallbackData(
-                TaskActions.Done.Label,
-                CallbackCodec.Encode(TaskActions.Done.Key, taskId))
-        );
-
-        await bot.SendMessage(
-            settings.OwnerChatId, Escape(text), ParseMode.Html, replyMarkup: keyboard, cancellationToken: ct);
-    }
+    public async Task SendTaskAsync(Guid taskId, string text, CancellationToken ct) =>
+        await bot.SendMessage(
+            settings.OwnerChatId, Escape(text), ParseMode.Html, replyMarkup: BuildTaskKeyboard(taskId),
+            cancellationToken: ct);

     /// <inheritdoc/>
     /// <remarks>
     /// Renders completion by wrapping the escaped text in an inline &lt;s&gt; element -- this
     /// adapter's own choice of how to show completion, not part of the interface's contract. The
     /// edit also sends <see cref="NoButtons"/>, an explicit empty keyboard, so a completed
-    /// reminder does not keep a dead Done button visible under its struck-through title.
+    /// reminder does not keep a dead button visible under its struck-through title.
     /// </remarks>
     public async Task MarkCompletedTaskAsync(int messageId, string text, CancellationToken ct) =>
         await bot.EditMessageText(
             settings.OwnerChatId, messageId, $"<s>{Escape(text)}</s>", ParseMode.Html, NoButtons,
             cancellationToken: ct);

+    /// <inheritdoc/>
+    /// <remarks>
+    /// Re-attaches the same keyboard <see cref="SendTaskAsync"/> would build for
+    /// <paramref name="taskId"/>, so a task that is still open keeps every action reachable from
+    /// the message that announces it -- unlike <see cref="MarkCompletedTaskAsync"/>, which sends
+    /// <see cref="NoButtons"/> because a completed task has nothing left to act on.
+    /// </remarks>
+    public async Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct) =>
+        await bot.EditMessageText(
+            settings.OwnerChatId, messageId, Escape(text), ParseMode.Html, BuildTaskKeyboard(taskId),
+            cancellationToken: ct);
+
+    // Both buttons in one row via the InlineKeyboardButton[] constructor overload, verified by
+    // reflecting the installed Telegram.Bot 22.10.2.1 metadata: "Creates an InlineKeyboardMarkup
+    // with multiple buttons on one row." Built by hand, not by iterating TaskActions.All --
+    // Schedule's callback data must carry a specific preset argument ("+1h"), which is a property
+    // of which button is being drawn, not of the action definition itself, so a generic loop here
+    // would need to invent an "argument per definition" concept this slice has no second user for.
+    // parse_mode governs the message body, not a button's own text, which Telegram carries as a
+    // plain JSON string rather than parsed markup -- so a label containing "&" or "<" would still
+    // need no escaping here.
+    private static InlineKeyboardMarkup BuildTaskKeyboard(Guid taskId) =>
+        new(new[]
+        {
+            InlineKeyboardButton.WithCallbackData(
+                TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, taskId)),
+            InlineKeyboardButton.WithCallbackData(
+                TaskActions.Schedule.Label, CallbackCodec.Encode(TaskActions.Schedule.Key, taskId, "+1h")),
+        });
+
```

In `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, register `ScheduleAction` and correct
`AddAssistantListener`'s `<remarks>`:

```diff
     /// <remarks>
     /// Requires <c>AddAssistantTelegram</c> for the client and the owner's chat id,
-    /// <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the failure backoff uses and
-    /// the <see cref="ITaskService"/> <see cref="Telegram.CallbackRouter"/>'s actions reach,
-    /// <c>AddAssistantTime</c> for the <see cref="ILocalTimeResolver"/>
-    /// <see cref="Telegram.MessageHandler"/> renders a stored due time back through, and
-    /// <c>AddAssistantAi</c> for the <see cref="IEnumerable{IAssistantTool}"/>
-    /// <see cref="Telegram.MessageHandler"/> dispatches a tool call against.
+    /// <c>AddAssistantServices</c> for the <see cref="TimeProvider"/> the failure backoff and
+    /// <see cref="Services.Actions.ScheduleAction"/>'s "+1h" preset both use and the
+    /// <see cref="ITaskService"/> every task action reaches,
+    /// <c>AddAssistantTime</c> for the <see cref="ILocalTimeResolver"/> both
+    /// <see cref="Telegram.MessageHandler"/> and <see cref="Telegram.CallbackRouter"/> render a
+    /// stored due time back through, and <c>AddAssistantAi</c> for the
+    /// <see cref="IEnumerable{IAssistantTool}"/> <see cref="Telegram.MessageHandler"/> dispatches a
+    /// tool call against.
     /// Handlers and task actions are registered scoped, not singleton, so
     /// <see cref="Telegram.TelegramListener"/> can resolve them from a scope it opens per update;
     /// see docs/tech-debt.md.
     /// </remarks>
     public static IServiceCollection AddAssistantListener(this IServiceCollection services)
     {
         services.AddScoped<ITelegramUpdateHandler, MessageHandler>();
         services.AddScoped<ITelegramUpdateHandler, CallbackRouter>();
         services.AddScoped<ITaskAction, DoneAction>();
+        services.AddScoped<ITaskAction, ScheduleAction>();
         services.AddHostedService<TelegramListener>();
```

```bash
dotnet build --no-restore
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 7: Run the whole suite — everything green**

```bash
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: **59 passed** unit (0 failed). **78 passed** integration (0 failed), including
`ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly`, now green because
`ScheduleAction` is both implemented and registered.

- [ ] **Step 8: Repeat the new Schedule/Reschedule tests in isolation for flakiness**

```bash
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~PlusOneHour|FullyQualifiedName~RescheduleAsync"
```

Run this three times. Expected each time: **7 passed**, 0 failed (three `CallbackRouterTests`
facts, four `TaskServiceTests` facts) — confirmed in this planning session's own worktree, no
flakiness across three runs.

- [ ] **Step 9: Correct spec §6.4's button table and its stale `EditAction` sentence**

In `docs/design/slice-1-reminders.md`, replace the table and the sentence immediately after it:

```diff
 | Button | Action | Effect |
 | :--- | :--- | :--- |
 | `Done` | `DoneAction` | `CompleteAsync`; message edited to show it struck through, buttons removed |
-| `Snooze 1h` | `SnoozeAction` (arg `1h`) | `SnoozeAsync(1h)`; clears `ReminderSentAt` so it fires again |
-| `Tomorrow` | `RescheduleAction` (arg `tomorrow`) | Moves `DueAt` to 09:00 Jerusalem the next day |
-| `Edit` | `EditAction` | Replies asking what to change; the next free-text message is routed to `update_task` for that task ID |
+| `+1h` | `ScheduleAction` (arg `+1h`) | `RescheduleAsync` to one hour from now; clears `ReminderSentAt` so it fires again |

-`EditAction` is the only one that costs an LLM call, and only on the follow-up message.
+**Corrected at F11-1:** snooze and reschedule are one operation, not the `SnoozeAction`/
+`RescheduleAction` split this table originally described — both set `DueAt` and clear
+`ReminderSentAt` through the same `ScheduleAction`/`ITaskService.RescheduleAsync`, whether the new
+time is relative (`+1h`) or absolute (`Tonight 20:00`). `EditAction` is dropped entirely: F10-4
+already deletes the owner's own message once a capture succeeds, so there is no message left to
+edit in place — retyping the task is the edit. F11-1 ships exactly one preset, `+1h`, on a flat
+`[Done] [+1h]` keyboard, proving the whole chain end to end; the `Schedule` button that swaps to a
+preset menu (`+3h`, `Tonight 20:00`, `Tomorrow 09:00`, `Back`) is F11-2's.
```

- [ ] **Step 10: Correct spec §4.2's `RescheduleAsync`/`DeliveryAttempts` bullet**

In the same file, in §4.2's bulleted list:

```diff
-- Snooze and reschedule will clear `ReminderSentAt` and reset `DeliveryAttempts`, so the task fires
-  again — the shape this pairing takes once `DeliveryAttempts` returns (F11); today only `MarkReminderSentAsync` sets `ReminderSentAt`, and there is no `DeliveryAttempts` column yet. **This pairing is the reason a single writer is mandatory** — setting one without the other silently stops a task from ever reminding again.
+- Snooze and reschedule clear `ReminderSentAt`, so the task fires again — implemented at F11-1 as
+  `RescheduleAsync`. **Corrected at F11-1:** `DeliveryAttempts` does not return with it, despite
+  this bullet's own original claim — a retry count is a delivery concern, not a due-time concern,
+  and belongs to F13 alongside the rest of that mechanism. **This pairing is the reason a single
+  writer is mandatory** — setting `DueAt` without clearing `ReminderSentAt` (or the reverse)
+  silently stops a task from ever reminding again.
```

- [ ] **Step 11: Correct the backlog's F11 entry**

In `docs/design/2026-08-22-slice-1-feature-backlog.md`, replace the F11 entry in full:

```diff
-**F11 · Snooze and reschedule** — spec §6.4, §4.2
-`SnoozeAction`, `RescheduleAction`, `EditAction`. Both clear `ReminderSentAt` and reset
-`DeliveryAttempts` so the task fires again — the pairing that makes `TaskService` the mandatory
-single writer. `ReminderTask` regains `DeliveryAttempts`; the retry cap enters the due query here.
-*Tests:* snooze 1h fires at exactly +1h, not immediately; the sent marker is cleared.
+**F11 · The clock moves · observable** — spec §6.4, §4.2 · split across two pull requests
+`ScheduleAction`, not the `SnoozeAction`/`RescheduleAction`/`EditAction` split this entry
+originally named. Snooze and reschedule are one operation: `RescheduleAsync` sets `DueAt` and
+clears `ReminderSentAt`, whether the new time is relative (`+1h`) or absolute (`Tonight 20:00`) —
+the pairing that makes `TaskService` the mandatory single writer. `EditAction` is dropped: F10-4
+already deletes the owner's own message on a successful capture, so there is no message left to
+edit in place — retyping the task is the edit. `ReminderTask` does **not** regain
+`DeliveryAttempts` here, despite this entry's own original claim — a retry count is F13's delivery
+concern, not a due-time concern.
+*Tests:* +1h fires at exactly one hour from now, not immediately; the sent marker is cleared.
+*Settled at F11-1:*
+- **Split into F11-1 and F11-2.** F11-1 ships one action (`ScheduleAction`) and one preset
+  (`+1h`) on a flat `[Done] [+1h]` keyboard, proving the whole chain end to end — tap, reschedule,
+  rewritten message, reminder fires again — on a real phone. F11-2 adds the remaining presets
+  (`+3h`, `Tonight 20:00`, `Tomorrow 09:00`) and the `[Schedule]` submenu spec §6.4 describes; F11
+  stays open, unmet by the `observable` tag, until it lands.
+- **`CallbackCodec` gains an optional fourth callback-data segment**,
+  `v1:<action>:<base64-id>[:<arg>]`, decoded to an empty string for every button that predates
+  F11-1 so nothing already in the owner's chat history breaks. `ITaskAction.ExecuteAsync` was
+  modified in place to carry it, not given a second, argument-taking sibling interface.
+- **The router renders by the task's resulting `Status`, not by which action ran.**
+  `ITaskAction.ExecuteAsync` and `ITaskService.CompleteAsync` now return the task itself;
+  `CallbackRouter` strikes it through on `Completed` and otherwise re-renders the message text and
+  keyboard in place from that task, so a future action inherits correct rendering with no change
+  to the router at all.
+- **This is the fix for half of issue #27** ("an undated task is stored but can never fire"): `+1h`
+  on a task with no due time gives it one for the first time. The issue itself stays open — there
+  is still no listing, so an undated task is only reachable through a button on its own capture
+  message.
```

- [ ] **Step 12: Correct the backlog's `DeliveryAttempts` deferred-property row**

In the same file, in §4's table:

```diff
-| `DeliveryAttempts` | F11 |
+| `DeliveryAttempts` | F13 — corrected at F11-1; a delivery-retry concern, not a due-time concern (see the F11 entry above) |
```

- [ ] **Step 13: Add the real-phone verification paragraph to `docs/e2e-local.md`**

In `docs/e2e-local.md`, after the existing Done-button paragraph and before `## Troubleshooting`:

```diff
 This is the one step in this file a test suite cannot substitute for: `CallbackRouterTests` and
 `TelegramNotifierTests` already prove every wire shape byte for byte, but only tapping a real
 button on a real phone proves Telegram itself renders one. This step is the owner's own to run --
 no agent may run the worker against real Telegram (it needs a real bot token and sends a real
 message) -- and it is F6's own `observable` requirement (backlog §1).

+The reminder also carries a +1h button now. Tap it: the message rewrites in place to show a due
+time one hour from now, and the same two buttons stay attached. `CallbackRouterTests` already
+proves every wire shape byte for byte, but only tapping a real button on a real phone proves
+Telegram itself renders the rewritten message -- the same posture the Done-button paragraph above
+already established for F6. Wait for that new time to pass (or seed a task already overdue, tap
++1h on it, and wait up to 30 seconds past the hour) and the reminder fires again, proving
+`RescheduleAsync` actually cleared the reminder-sent marker, not only in a test database. This is
+F11-1's own `observable` proof (backlog §1).
+
 ## Troubleshooting
```

- [ ] **Step 14: Final build and full suite, once more, after the documentation edits**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **59 passed** unit. **78 passed**
integration. Documentation changes do not touch any `.cs` file, so this is a confirmation step, not
expected to differ from Step 7.

- [ ] **Step 15: Commit**

```bash
git add src/Assistant.Contracts/ErrorCode.cs \
        src/Assistant.Contracts/TaskActions.cs \
        src/Assistant.Interfaces/ITaskAction.cs \
        src/Assistant.Interfaces/ITaskService.cs \
        src/Assistant.Interfaces/INotifier.cs \
        src/Assistant.Impl/Services/TaskService.cs \
        src/Assistant.Impl/Services/Actions/DoneAction.cs \
        src/Assistant.Impl/Services/Actions/ScheduleAction.cs \
        src/Assistant.Impl/Telegram/CallbackCodec.cs \
        src/Assistant.Impl/Telegram/CallbackRouter.cs \
        src/Assistant.Impl/Telegram/TelegramNotifier.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        src/Assistant.Impl/Telegram/ReminderTaskTextExtensions.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs \
        tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs \
        tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs \
        tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs \
        docs/design/slice-1-reminders.md \
        docs/design/2026-08-22-slice-1-feature-backlog.md \
        docs/e2e-local.md
git commit
```

Message:

```
feat: F11-1 -- the clock moves

Every task message now carries two buttons, [Done] [+1h], not one.
Tapping +1h moves the task's due time to one hour from now, clears
its reminder-sent marker, and rewrites the message in place to show
the new time -- the chat stays a truthful list of open tasks rather
than one showing a stale time. This is the smallest slice that
proves the whole chain end to end and is verifiable on a real phone;
the [Schedule] submenu and its remaining presets (+3h, Tonight
20:00, Tomorrow 09:00) are F11-2's.

Snooze and reschedule are one operation, ScheduleAction /
ITaskService.RescheduleAsync, not the SnoozeAction/RescheduleAction
split spec 6.4 originally named. EditAction is dropped entirely:
F10-4 already deletes the owner's own message on a successful
capture, so retyping the task is the edit.

CallbackCodec's callback_data format gains an optional fourth
segment, decoded to an empty string for every button that predates
this slice so nothing already in the owner's chat history breaks.
ITaskAction.ExecuteAsync was modified in place to carry the decoded
argument, and ITaskService.CompleteAsync now returns the task itself
alongside the new RescheduleAsync, both with the identical shape --
neither interface gained a speculative sibling for the one new
capability. CallbackRouter renders by the task's resulting Status,
never by which action ran: a Completed task strikes through and
loses its keyboard exactly as before, and any other status is
re-rendered in place with its keyboard still attached and its text
rebuilt fresh from the task, so a future action inherits correct
rendering with no change to the router at all. INotifier gains
UpdateTaskAsync for that re-render, distinct from
MarkCompletedTaskAsync, which unconditionally clears the keyboard.

An unrecognised callback argument is refused with a new ErrorCode,
TaskActionArgumentUnrecognized, the same posture ModelNamedUnknownTool
already established for an unregistered tool name.

+1h on a task with no due time gives it one for the first time --
the fix for half of issue #27, which stays open pending a listing
surface. No migration: DueAt and ReminderSentAt are both already
nullable columns.

Two existing tests (TelegramListenerTests, DueReminderJobTests)
needed updating for the two-button keyboard; one existing unit test
row asserted a four-segment callback string must fail to decode,
now replaced with a five-segment one.

Spec 6.4's button table and 4.2's DeliveryAttempts bullet are
corrected in the same commit; the backlog's F11 entry is split into
F11-1/F11-2 and its DeliveryAttempts row corrected to F13.
e2e-local.md gains the +1h real-phone verification paragraph
alongside the existing Done one.

Tests: 59 unit, 78 integration, up from 57 / 70. Build clean, zero
warnings.

Plan: docs/plans/2026-09-06-f11-1-the-clock-moves.md

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `TaskActionArgumentUnrecognized` is appended after `ModelNamedUnknownTool`, not inserted
      (Decision 6)
- [ ] `TaskActions.Schedule` is declared before `All`, `Label` is `"+1h"` not `"Schedule"`
      (Decision 12's `<remarks>`)
- [ ] `CallbackCodec.TryDecode` accepts exactly three or four segments, never any other count;
      `Encode` has two overloads, no default parameter (Decision 1)
- [ ] `ITaskAction.ExecuteAsync`'s signature is exactly `(Guid taskId, string argument,
      CancellationToken ct)` returning `Task<Result<ReminderTask>>` — no second interface exists
      anywhere in the diff (Decision 2)
- [ ] `ITaskService.CompleteAsync` returns `Task<Result<ReminderTask>>`; `RescheduleAsync`'s
      signature is exactly `(Guid id, DateTimeOffset dueAtUtc, CancellationToken ct)` (Decision 3)
- [ ] `RescheduleAsync` and `CompleteAsync` both refuse an already-completed task with
      `ErrorCode.TaskAlreadyCompleted`, never silently succeeding (Decision 3)
- [ ] `ScheduleAction`'s primary constructor takes exactly `(ITaskService taskService, TimeProvider
      timeProvider)` — no `ILocalTimeResolver` parameter anywhere on this type (Decision 4)
- [ ] `ScheduleAction.ExecuteAsync` compares `argument` to the literal `"+1h"` and refuses anything
      else with `ErrorCode.TaskActionArgumentUnrecognized`, never throwing and never calling
      `RescheduleAsync` for an unrecognised argument (Decision 5)
- [ ] `CallbackRouter.HandleAsync` names neither `TaskActions.Done` nor `TaskActions.Schedule`
      anywhere in its executable code — only `ReminderStatus.Completed` decides which notifier
      method runs (Decision 10)
- [ ] `INotifier.UpdateTaskAsync`'s signature is exactly `(int messageId, Guid taskId, string text,
      CancellationToken ct)` — no `InlineKeyboardMarkup` or other Telegram type anywhere in
      `Assistant.Interfaces` (Decision 11)
- [ ] `TelegramNotifier.BuildTaskKeyboard` uses the `InlineKeyboardButton[]` constructor overload
      and produces one row containing both buttons, Done first — verified against the installed
      package, not assumed (Decision 12, "Verified facts")
- [ ] `ReminderTaskTextExtensions.ToMessageText` lives in `Assistant.Impl.Telegram`, is `internal`,
      and is the only place `DueTimeFormat` is declared (Decision 8)
- [ ] `MessageHandler.HandleAsync`'s only change is the one-line reply-construction extraction;
      no other line of its control flow differs from before this slice (Decision 8)
- [ ] `ImplServiceCollectionExtensions.AddAssistantListener` registers `ScheduleAction` as
      `ITaskAction` alongside `DoneAction`
- [ ] No `Assistant.Models`, `Assistant.Repository`, or migration file appears in the diff
      (Decision 7, "Verified facts")
- [ ] `TelegramStubs.cs` is not in the diff, and no `docker compose ... --build` command appears
      anywhere in this plan's own commands (Decision 12)
- [ ] `DueReminderJobTests.cs` and `TelegramListenerTests.cs`'s updated tests assert both button
      labels and both callback-data strings, in order, not merely the row's count (Decision 13)
- [ ] `CallbackCodecTests.cs`'s replaced theory row is five segments, not four, and still asserts
      failure (Decision 14)
- [ ] Every new or changed public member carries a three-line-tag `<summary>` plus every
      `<param>`/`<returns>` `CS1591`/`CS1573` requires
- [ ] Test summaries are Gherkin (`When`/`And`/`Then`), one clause per line, in every new test
- [ ] No emoji anywhere, including the commit message
- [ ] **No plan-internal decision citation (`Decision 1`, `(Decision 2)`, or similar) inside any
      C# code block, doc comment, or commit message** — every fenced code block above was re-read
      for this before the plan was committed
- [ ] Plain ASCII `--` used inside every C# doc comment, C# comment, and the commit message body;
      every markdown-prose block destined for `slice-1-reminders.md`, the backlog, or
      `e2e-local.md` uses real em dashes, matching those documents' own established convention
      (verified by grepping each for the `—` character before drafting a single addition)
- [ ] `docs/plans/2026-09-04-f6-2-action-catalogue.md`, `docs/plans/2026-09-03-f6-2-route-the-tap.md`,
      `docs/plans/2026-09-04-f6-3-the-button-appears.md`, and both pre-YAGNI-reset
      `docs/2026-08-16-*.md` documents are untouched (Decision 15)
- [ ] Spec §3.4 and §3.6 are untouched (Decision 15)

**Whole feature (F11), once this lands:**
- [ ] The backlog's F11 entry carries `· observable` in its header and a `*Settled at F11-1:*`
      block, but no `done` marker — it stays open until F11-2 (Decision 15)
- [ ] Spec §6.4's table shows exactly `Done` and `+1h`, with a `**Corrected at F11-1:**` paragraph
      explaining the three-action-to-one-action change (Decision 15)
- [ ] This slice's diff measures 740 lines by the convention argued in "How this slice fits" —
      comfortably under the 1000-line budget on its own
