# F11-3 — the button appears

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F11-1 (`8989d6c`) and F11-2 (`cc8f82d`) built `RescheduleAsync`, `CallbackCodec`'s
fourth segment, and `ITaskAction`'s widened, argument-carrying shape — three pieces of plumbing
with no caller outside their own tests. This slice is F11's third pull request, and the first with
an observable effect: it gives every task message a second button, `+1h`, backed by a new
`ScheduleAction` that calls `RescheduleAsync`; teaches `CallbackRouter` to render a tap's result by
the task's own `Status` rather than by which action produced it; and extracts the reply-text
renderer both `MessageHandler` and `CallbackRouter` now need. After this merges, the owner can tap
a real button on a real phone and watch a task's due time move.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **no new NuGet package** and **no database migration**: `RescheduleAsync` already persists
everything `ScheduleAction` needs (F11-1), and no new column is touched.

**Spec:** `docs/design/slice-1-reminders.md` §4.2 (`TaskService` as single writer, corrected here
— see Decision 8), §6.4 (inline buttons — corrected here, the first slice entitled to since it is
the first to render a second one), §7.2 (unit vs. integration split), §7.3 (assertion standard),
§12.1 (XML docs), §12.5 (primary constructors), §12.6 (no emoji). §3.4 and §3.6 are read but left
uncorrected — see Decision 8.

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — the F11 entry and the
`DeliveryAttempts` deferred-property row are both corrected here (Decision 8); F11-1 and F11-2
both left them alone for the reason their own plans state: nothing was observable yet.

---

## How this slice fits

F11 splits into four independently reviewable pull requests, in a strict dependency order forced
by what compiles against what:

1. **F11-1 (merged, `8989d6c`) — the writer moves the clock.** `ITaskService.RescheduleAsync` and
   `CallbackCodec`'s fourth segment. Nothing outside their own tests called either.
2. **F11-2 (merged, `cc8f82d`) — the action learns to carry an argument.** `ITaskAction.ExecuteAsync`
   widened to accept an argument and return the task; `ITaskService.CompleteAsync` widened to
   match. Still no button, no renderer.
3. **F11-3 (this plan) — the button appears.** `TaskActions.Schedule` and `ScheduleAction`;
   `INotifier.UpdateTaskAsync` and `TelegramNotifier`'s two-button keyboard; the shared
   `ToMessageText` renderer; `CallbackRouter`'s switch to status-based rendering and the
   four-argument `TryDecode` overload; the DI registration; the three tests that hard-code one
   button; every deferred documentation correction.
4. **F11-4 — the menu.** `Schedule` becomes a menu opener; `+3h`, `Tonight 20:00`,
   `Tomorrow 09:00`, and `Back` join `+1h` as presets.

**Why this is the biggest of the four.** F11-1 and F11-2 each built one interconnected piece of
plumbing nothing yet called. This slice is where every one of those pieces gets its first real
caller at once — `ScheduleAction` calls `RescheduleAsync`; `CallbackRouter` calls the argument
through to it and reads the four-segment codec; `TelegramNotifier` needs a keyboard builder that
did not exist; two renderers (`MessageHandler`'s and the new `CallbackRouter` branch) need to agree
on one string. None of that interconnection can be split further without recreating the
hard-to-review single commit the four-way split exists to avoid — see Decision 6 for the router
rewrite this forces, and "Steps" for why it lands as one commit.

**The measured total, drafted in full, built, and tested green — not estimated.** Every line below
was produced by actually writing this slice's sixteen files in an isolated `git worktree` branched
from this repository's own `HEAD` (`cc8f82d`), following the exact sequence given in "Steps," then
reading `git diff --numstat` against it directly:

| File | + | - |
| :--- | ---: | ---: |
| `src/Assistant.Contracts/ErrorCode.cs` | 5 | 0 |
| `src/Assistant.Contracts/TaskActions.cs` | 22 | 5 |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | 6 | 4 |
| `src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs` | 23 | 0 |
| `src/Assistant.Impl/Services/Actions/ScheduleAction.cs` | 45 | 0 |
| `src/Assistant.Impl/Telegram/CallbackRouter.cs` | 42 | 9 |
| `src/Assistant.Impl/Telegram/MessageHandler.cs` | 2 | 8 |
| `src/Assistant.Impl/Telegram/TelegramNotifier.cs` | 37 | 23 |
| `src/Assistant.Interfaces/INotifier.cs` | 25 | 0 |
| **Code subtotal** | **207** | **49** |
| `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs` | 11 | 8 |
| `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs` | 109 | 1 |
| `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs` | 14 | 10 |
| `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs` | 39 | 0 |
| **Test subtotal** | **173** | **19** |
| `docs/design/2026-08-22-slice-1-feature-backlog.md` | 18 | 7 |
| `docs/design/slice-1-reminders.md` | 10 | 7 |
| `docs/e2e-local.md` | 8 | 0 |
| **Docs subtotal** | **36** | **14** |
| **Grand total** | **416** | **82** |

**Total changed lines: 498** (416 insertions + 82 deletions), 502 lines under the 1000-line budget.

**This runs higher than the 350-420 line range this slice was expected to land in — stated plainly,
not glossed over.** `CallbackRouterTests.cs` alone is 110 of the 498 lines, because Decision 6's
"render by status" claim and Decision 7's "the too-old guard does not protect `UpdateTaskAsync`"
claim each demand a scenario nothing else in the suite proves — three new tests, not one.
`TelegramNotifier.cs` and `CallbackRouter.cs` both carry `<remarks>` this brief asked to be "argued
at length" in the code itself, not only here. The documentation corrections (50 lines) are in scope
for the first time in F11, per Decision 8. None of it is padding; 502 lines of headroom remain
either way.

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** `ScheduleAction`, and the widened
  `CallbackRouter`, both use one; no separate constructor is introduced anywhere.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- No emoji anywhere: source, tests, docs, or commit messages.
- Enums: first member is `Unknown`, no explicit numeric values, new members **appended**, never
  inserted. `ErrorCode.TaskActionArgumentUnrecognized` is appended after `ModelNamedUnknownTool`,
  the enum's current last member.
- **Never run `docker compose down -v`.** No image rebuild or new container is needed — see
  "Validation" for why the existing WireMock stub already answers every endpoint this slice needs.
  If nothing is running, `docker compose -f compose.test.yaml up -d` (no `--build`) is enough.
- PR budget: 1000 changed lines per PR, excluding the plan document. This slice measures at 498
  lines — 502 lines under budget, discussed above.
- Plain ASCII `--` in every C# file; real em dashes in this document's own markdown prose.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was checked directly against the working tree at `cc8f82d` (`HEAD` of
`feature/f11-3-the-button-appears`) or produced by a command actually run during this planning
session.

- **`TaskActions.cs`'s only entry is `Done`, and `All` is `[Done]`.** `src/Assistant.Contracts/TaskActions.cs:18-26`.
  Its own `<remarks>` (lines 8-11) documents the static-initializer-ordering rule this slice's new
  `Schedule` entry must also respect: declared before `All`, whose own initializer reads it.
- **`ErrorCode`'s last member is `ModelNamedUnknownTool`**, at `src/Assistant.Contracts/ErrorCode.cs:74`.
  `TaskActionArgumentUnrecognized` is appended after it; no explicit numeric value anywhere in the
  enum.
- **`CallbackRouter.cs`'s three-argument `TryDecode` call is its only one**, at
  `src/Assistant.Impl/Telegram/CallbackRouter.cs:85`, and `ExecuteAsync` is called with a literal
  `string.Empty` at line 99 (F11-2 Decision 4). The success branch at lines 101-104 reads
  `result.IsSuccess && messageText is not null` and calls only `MarkCompletedTaskAsync` — no branch
  reads `result.Value`. The class carries no `ILocalTimeResolver` today.
- **`CallbackCodec`'s four-argument `TryDecode` and three-argument `Encode` already exist and are
  fully tested** (F11-1): `src/Assistant.Impl/Telegram/CallbackCodec.cs:41-42` (`Encode` with an
  argument), `:71-103` (`TryDecode` with an `argument` out-parameter). This slice is their first
  production caller.
- **`ITaskAction.cs`'s `<remarks>` (lines 13-14) already name `ScheduleAction` and the `+1h` preset
  key** — written by F11-2 in anticipation of this slice — so `ITaskAction.cs` itself needs no
  change here.
- **`INotifier.cs` has three members: `SendAsync`, `SendTaskAsync`, `MarkCompletedTaskAsync`**
  (`src/Assistant.Interfaces/INotifier.cs:16-66`), the last ending the file. `UpdateTaskAsync` is
  appended after it.
- **`TelegramNotifier.SendTaskAsync` builds a single-button keyboard by hand**
  (`src/Assistant.Impl/Telegram/TelegramNotifier.cs:55-65`), and its own `<remarks>` (lines 41-53)
  already flag the exact question this slice must answer: "iterating would silently fix the layout
  at everything in one row — a decision that belongs to F11." The `NoButtons` comment immediately
  above the class (lines 29-33) documents the same overload trap this slice's two-button row must
  also avoid.
- **`Telegram.Bot` is pinned at `22.10.2.1`** (confirmed via `dotnet restore`). Reflecting
  `InlineKeyboardMarkup`'s public constructors against the installed assembly found six; the one
  taking `IEnumerable<InlineKeyboardButton> inlineKeyboardRow` wraps its whole argument as one row,
  distinct from the plural, already-nested `IEnumerable<IEnumerable<InlineKeyboardButton>>`
  overload. Serialising confirms: `new InlineKeyboardMarkup()` → `{"inline_keyboard":[]}`;
  the one-row overload given an empty array → `{"inline_keyboard":[[]]}` — one empty row, the trap
  `NoButtons` already names; given two buttons → `{"inline_keyboard":[[button1,button2]]}` — one
  row of two. This is the fact Decision 5 rests on, verified rather than assumed.
- **`tests/Assistant.WireMock/TelegramStubs.cs` already answers every endpoint this slice needs.**
  Its four mappings (`sendMessage`, `answerCallbackQuery`, `editMessageText`, `deleteMessage`, plus
  the low-priority `getUpdates` fallback) all match by HTTP path only
  (`Request.Create().WithPath(...)`), never by request body — so a two-button keyboard on
  `sendMessage`, or `UpdateTaskAsync`'s call to the same `editMessageText` endpoint
  `MarkCompletedTaskAsync` already uses, need no new mapping. **No `docker compose ...
  --build` is required, and the stub image is unchanged** — confirmed by running the full
  integration suite against the already-running, unmodified `personal-ai-assistant-wiremock-1`
  container.
- **`ReminderStatus` has four members: `Unknown`, `Pending`, `Completed`, `Cancelled`**
  (`src/Assistant.Models/ReminderStatus.cs`). `CallbackRouter`'s new branch treats exactly
  `Completed` as the struck-through case; `Pending`, `Cancelled`, and `Unknown` (unreachable in
  practice — `RescheduleAsync` never produces it) all fall into "anything else" and re-render.
- **`MessageHandler.cs` builds its reply text inline at lines 151-154**, exactly as F11-2's own
  plan measured it: `var task = outcome.Value!;` through the ternary building `reply`, then
  `notifier.SendTaskAsync(task.Id, reply, ct)` at line 156.
- **`docs/design/slice-1-reminders.md` §3.4 and §3.6 already diverge from the real repository on
  axes this slice does not touch.** §3.4's tree lists `Services/ ... MessageHandler` (it is under
  `Telegram/`) and `AgentService` (no such type exists anywhere in `src/`, confirmed by
  `grep -r "AgentService" src/`). §3.6's `IAssistantTool` row lists `CreateTask, ListTasks,
  UpdateTask, CompleteTask` as "Implementations in slice 1"; the real registration
  (`ImplServiceCollectionExtensions.cs:136`) has exactly one, `CreateTaskTool`. This is the fact
  Decision 8 rests on for leaving both sections alone.
- **Baseline test counts, run directly against this exact `HEAD`, not assumed.**
  `dotnet build --no-restore` — `Build succeeded. 0 Warning(s). 0 Error(s).` `dotnet test
  tests/Assistant.UnitTests --no-build` — **59 passed**, 0 failed. Against the already-running
  `personal-ai-assistant-postgres-test-1` and `personal-ai-assistant-wiremock-1` containers,
  `dotnet test tests/Assistant.IntegrationTests --no-build` — **74 passed**, 0 failed. Of those 74,
  `CallbackRouterTests.cs` contributes exactly **8** (6 `[Fact]` methods, one `[Theory]` with 2
  `[InlineData]` rows, and one non-async `[Fact]` — confirmed by
  `grep -c '\[Fact\]\|\[Theory\]'` and `grep -c '\[InlineData'` against the file directly),
  `TelegramNotifierTests.cs` contributes **5** (3 Facts, one Theory with 2 rows), and
  `DueReminderJobTests.cs` contributes **5** (5 Facts, no Theory).
- **This plan's own draft was built and tested green, not merely written.** All sixteen files
  below were applied inside an isolated `git worktree` branched from this repository's own `HEAD`
  (never touching this working tree's `src/`, `tests/`, or `docs/`), following the exact
  step-by-step sequence given in "Steps," and reached: `Build succeeded. 0 Warning(s).
  0 Error(s).`, **59 unit / 78 integration passed**. The full integration suite was re-run three
  times in total with no failure, and `CallbackRouterTests.cs` alone was re-run in isolation three
  further times (`dotnet test --filter "FullyQualifiedName~CallbackRouterTests"`), **11 passed**
  every time. The worktree and its branch were then removed; the real working tree's own build was
  independently rebuilt and re-tested afterward to confirm it was left exactly as found (**59
  unit / 74 integration**, unchanged).

---

## Inherited context: what this slice reads from earlier features

`ITaskService.RescheduleAsync` (F11-1) is called for the first time, unchanged, by `ScheduleAction`.
`CallbackCodec`'s four-argument `TryDecode` and three-argument `Encode` (F11-1) are read for the
first time by `CallbackRouter` and `TelegramNotifier` respectively, also unchanged. `ITaskAction`'s
widened `ExecuteAsync` signature and `ITaskService.CompleteAsync`'s widened return (F11-2) are
consumed as-is: `ScheduleAction` is the second implementation of the widened interface,
`DoneAction` needs no change. `TaskActions.Done` and `DoneAction` (F6-1/F6-2) are read, not
modified. `MessageHandler`'s existing reply construction (F9a-2 era, unchanged in shape since) is
extracted, not rewritten.

---

## Decisions

### 1. `TaskActions.Schedule`'s label is `"+1h"`, not `"Schedule"`

**Decision:**

```csharp
public static TaskActionDefinition Schedule { get; } = new(
    Key: "schedule",
    Label: "+1h",
    Description: "Moves the task's due time to one hour from now, arming a reminder for the "
        + "first time on a task that had none. Refused when the task is already complete, or "
        + "when the callback carries an argument other than \"+1h\".");
```

**Why, argued rather than asserted.** A button's label is the one thing a human reads before
tapping it — it has to say what tapping it does, right now, not what it will do once a later slice
gives it a menu. `Schedule` as a label would be true of the *feature*, but false of the *button*: a
button labelled `Schedule` implies choosing among options, and this slice offers exactly one,
applied directly on tap, with no intermediate screen. `+1h` says exactly and only that. The
alternative — label it `Schedule` now, in anticipation of F11-4's menu — would mean shipping a
button whose own text overpromises what a tap on it does today, the same category of drift
`AGENTS.md`'s "correct it in the same commit" rule exists to catch, just introduced deliberately
instead of by accident. `Done`'s own label follows the identical rule: it says the one thing
tapping it does, not a category of things a future slice might expand it into.

### 2. `ScheduleAction`: UTC-instant arithmetic, "+1h" means one hour from now, and no
   `ILocalTimeResolver` dependency

**Decision:**

```csharp
internal sealed class ScheduleAction(ITaskService taskService, TimeProvider timeProvider) : ITaskAction
{
    internal const string PlusOneHour = "+1h";

    public TaskActionDefinition Definition => TaskActions.Schedule;

    public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
        argument == PlusOneHour
            ? taskService.RescheduleAsync(taskId, timeProvider.GetUtcNow().AddHours(1), ct)
            : Task.FromResult(Result<ReminderTask>.Failure(ErrorCode.TaskActionArgumentUnrecognized));
}
```

**Why `timeProvider.GetUtcNow().AddHours(1)`, not `task.DueAt?.AddHours(1)` — the critical
correctness point.** A reminder that already fired carries a due time in the past — exactly the
state a `+1h` tap finds it in most often, since the button is still on screen *because* it already
notified the owner. Adding an hour to that stale time would still be in the past, or would fire
again almost immediately — neither is "one hour from now." `RescheduleAsync` clearing
`ReminderSentAt` (F11-1) is what lets it fire again at all; anchoring the new time to the *current*
instant is what makes that second firing land somewhere useful. `RescheduleAsync`'s own signature —
an already-resolved `dueAtUtc` — puts this choice on `ScheduleAction`, not the service.

**Why the current UTC instant, never a local wall-clock reading of it — the daylight-saving
argument.** `GetUtcNow()` plus a `TimeSpan` is unambiguous arithmetic on a real instant. Converting
to a local reading first, adding an hour to *that*, and converting back would be wrong on the two
nights a year Israel's clock changes: a spring-forward "01:30 plus one wall-clock hour" can name a
reading that never happens; a fall-back one can be zero or two real hours away depending which side
of the repeated hour it starts from. `ILocalTimeResolver`'s own contract
(`src/Assistant.Interfaces/ILocalTimeResolver.cs:36-54`) documents this exact ambiguity for
resolving; the same ambiguity applies to arithmetic done after converting back. `ScheduleAction`
sidesteps it entirely by taking `TimeProvider` directly and never touching a wall-clock reading —
the reason this slice needs no `ILocalTimeResolver` here. The presets that *do* need one
(`Tonight 20:00`, `Tomorrow 09:00`) are F11-4's, and need it because they are phrased in wall-clock
terms from the start, not because relative arithmetic like `+1h` ever does.

**Why an unrecognised argument is refused with a new `ErrorCode`, not coerced to `+1h`.**
`CallbackRouter` decodes whatever the wire carries (Decision 7) — a stale button from a future
release, or a crafted callback string, could carry any text. Refusing anything but the one
recognised value is the same discipline `CallbackCodec.TryDecode` already applies to a malformed
wire string: fail explicitly rather than guess. `TaskActionArgumentUnrecognized` is appended to
`ErrorCode`, never inserted.

**`+1h` on a task with no due time at all works — the real fix for the gap issue #27 named,
mentioned but not closed.** `RescheduleAsync` already accepts a `null` `DueAt` and gives it one for
the first time (F11-1 Decision 3), proven by
`RescheduleAsync_TaskHadNoDueTime_GivesItADueTimeForTheFirstTime`. `ScheduleAction` calls it the
same way regardless of whether a due time already existed. Issue #27 also covers listing undated
tasks, a capability nothing here adds, so it stays open.

**No `ScheduleActionTests.cs` unit test, for the same reason `DoneAction` got none at F11-2.**
`ScheduleAction`'s two outward-visible outcomes — a recognised argument reschedules, an
unrecognised one is refused — are fully exercised by
`Listener_OwnerTapsSchedule_MovesTheDueTimeOneHourFromNowAndUpdatesTheMessage` and
`Listener_ScheduleTappedWithAnUnrecognisedArgument_AnswersButLeavesTheDueTimeUnchanged`; a unit test
asserting the same facts through a different route would duplicate coverage `AGENTS.md` rules out.
This codebase still has no mocking library and no hand-rolled fake anywhere under
`tests/Assistant.UnitTests`. And the one thing a unit test could add — proving
`timeProvider.GetUtcNow()` is read, not `DateTimeOffset.UtcNow` — is already observable at the
integration level: `Listener_OwnerTapsSchedule_...` seeds a `FakeTimeProvider` and asserts the exact
resulting `DueAt`, which would fail immediately against the real clock.

### 3. The shared `ToMessageText` renderer is an extension method on `ReminderTask`, in
   `ReminderTaskMappingExtensions`, extracted now that a second caller exists

**Decision:**

```csharp
public static string ToMessageText(this ReminderTask task, ILocalTimeResolver clock) =>
    task.DueAt is { } dueAt
        ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
        : $"{task.Title} -- saved with no reminder.";
```

placed in `src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs`, beside the existing
`ToModel`.

**Why an extension method, not a small standalone type.** `ReminderTaskMappingExtensions` already
exists for exactly this purpose — spec §12.2's "mapping is extension methods named by destination"
— and already holds `ToModel`, a `CreateTaskRequest → ReminderTask` mapper living beside a
`ReminderTask → string` one is the same file doing the same job in both directions the class's own
summary already claims: "the two genuinely external boundaries a task crosses." A new type (an
`IMessageRenderer`, say) would be machinery for a single pure function with one dependency it
already receives as a parameter — nothing here has more than one implementation, ever, and nothing
needs to be swapped at runtime. `ToMessageText(this ReminderTask, ILocalTimeResolver)` reads at the
call site exactly the way `ToModel` already does: `task.ToMessageText(clock)`.

**Why this extraction happens now, correcting F11-2's own deferral.** F11-2's Decision 5 argued, at
length, that extracting this renderer before a second caller existed would prove nothing an
existing test did not already prove, and would risk nobody catching a subtly wrong shape until this
slice anyway. Both of those conditions are gone: `CallbackRouter` is now the second caller
(Decision 6), and its own new tests assert the exact rendered string
(`"call the bank -- due Tuesday 25 August 2026, 16:00."`), which is the first assertion anywhere in
the suite able to prove the extraction faithful rather than merely plausible — exactly the trigger
F11-2's own Decision 5 named as the earliest defensible point to do this.

**Why it still earns no new unit test.** The two branches — a due instant set, or not — are already
fully characterised by `TelegramListenerTests.cs`'s two capture-reply tests (through
`MessageHandler`) and now also by `CallbackRouterTests.cs`'s new Schedule scenario (through
`CallbackRouter`). `AGENTS.md`'s rule against a unit test for behaviour an integration test already
covers applies exactly as it did to `DoneAction` — see Decision 2.

### 4. `INotifier.UpdateTaskAsync`: a new method, distinct from `MarkCompletedTaskAsync`, taking
   plain text and a channel-neutral task id

**Decision:**

```csharp
Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct);
```

**Why a fourth method, not an optional parameter on `MarkCompletedTaskAsync`.**
`MarkCompletedTaskAsync`'s whole contract is "this task is finished": it wraps the text in `<s>`
and sends an explicit empty keyboard, unconditionally, per its own existing `<remarks>`.
`UpdateTaskAsync`'s contract is the opposite in both particulars — no strike-through, and the
keyboard stays attached, rebuilt for the same task. Bolting a `bool stillPending` flag onto one
method to switch between two mutually exclusive renderings would make every call site read the flag
to know what actually happens, where two names read at the call site with no need to open the
implementation. This is the same reasoning `INotifier`'s own `<remarks>` already gives for why
`SendTaskAsync` takes a task id at all: a channel-neutral handle the adapter needs to build its own
affordance, never a pre-built, channel-specific one.

**Why it takes plain unescaped text and a task id, never a model and never a pre-built
`InlineKeyboardMarkup` — honouring `INotifier`'s existing contract exactly, as the brief demands.**
`INotifier`'s own class-level `<remarks>` already state the rule this method must not break:
"Rendering the message body is the caller's job -- a notifier escapes and formats text it is given,
never composing prose of its own," and a task identifier is "the channel-neutral handle an adapter
needs to build whatever affordance its own channel supports... not a database shape." Passing
`ReminderTask` itself would let `TelegramNotifier` reach into fields no other channel's affordance
needs (`CreatedAt`, `ReminderSentAt`) and would import `Assistant.Models` into a decision that is
purely "what string, what id" — the same reasoning `SendTaskAsync` already settled. Passing an
`InlineKeyboardMarkup` would leak Telegram's own wire type into `Interfaces`, which references
`Models` and `Contracts` but must stay free of `Telegram.Bot` (architecture-tested, §3.2) — a
future channel with no concept of an inline keyboard would have no way to implement this interface
at all.

### 5. `TelegramNotifier`'s two-button keyboard: built by hand, one row, verified against the
   installed `Telegram.Bot` 22.10.2.1 metadata

**Decision:**

```csharp
private static InlineKeyboardMarkup BuildTaskKeyboard(Guid taskId) => new(
    new[]
    {
        InlineKeyboardButton.WithCallbackData(
            TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, taskId)),
        InlineKeyboardButton.WithCallbackData(
            TaskActions.Schedule.Label,
            CallbackCodec.Encode(TaskActions.Schedule.Key, taskId, ScheduleAction.PlusOneHour)),
    });
```

shared by both `SendTaskAsync` and the new `UpdateTaskAsync`.

**Both questions the existing `<remarks>` left open, answered together.** `SendTaskAsync`'s own
comment already named what F11 would have to decide: whether iterating `TaskActions.All` would
"silently fix the layout at everything in one row," and, implicitly, how a per-button argument like
`Schedule`'s `+1h` would reach an iteration over `TaskActionDefinition` records that carry no
argument field. Both answer to the same choice: build both buttons by hand. Adding a fourth field
to `TaskActionDefinition` for "the wire argument this button always sends" would be true only for
`Schedule`, forcing `Done` (and whatever F11-4 adds) to carry a meaningless value — the same
"fields exist only where every consumer needs them" principle `TaskActions` already states for
`Description`. This also defers the loop-versus-hand-build question again for F11-4, which will
decide it for real once `Schedule` becomes a variable-width row of presets.

**Why the `IEnumerable<InlineKeyboardButton>` overload, confirmed rather than guessed, produces one
row of two.** Reflecting `Telegram.Bot` 22.10.2.1's `InlineKeyboardMarkup` (see "Verified facts")
found six public constructors; the one taking `IEnumerable<InlineKeyboardButton> inlineKeyboardRow`
(singular) wraps its whole argument as **one row**, distinct from the one taking
`IEnumerable<IEnumerable<InlineKeyboardButton>> inlineKeyboard` (plural), an already-nested grid.
Passing the two-button array to the first produces `{"inline_keyboard":[[doneButton,scheduleButton]]}`
on the wire — confirmed by actually serialising it, not inferred from the parameter name. This is
the same overload `SendTaskAsync` already used for one button; widening it to two changes only how
many elements sit inside the one row it always produces.

**`ScheduleAction.PlusOneHour`, not a second string literal.** Duplicating `"+1h"` in two files
invites exactly the drift a single source of truth prevents. `ScheduleAction` defines what argument
it understands, so it owns the constant; `internal` is enough, since both types live in
`Assistant.Impl`.

### 6. `CallbackRouter` renders by the task's resulting `Status`, never by which action ran —
   the central decision this slice makes, argued at length

**Decision:**

```csharp
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
```

**The problem this replaces.** Before this slice, the success branch was
`if (result.IsSuccess && messageText is not null) { await notifier.MarkCompletedTaskAsync(...); }`
— correct only because `DoneAction` was the sole registered action, so every success *was* a
completion. `ScheduleAction`'s success never completes anything; it moves a due time. The old
branch, unchanged, would strike the message through and delete its keyboard on a `+1h` tap too —
the opposite of what tapping the button should do. Something has to decide, per tap, which edit is
correct.

**Two alternatives considered and rejected, each on its own terms.**

*Rejected: a switch on the action's own key* (`actionKey == "done" ? MarkCompletedTaskAsync(...) :
UpdateTaskAsync(...)`). This compiles today and would pass every test this slice adds. It is
rejected because it makes `CallbackRouter` the one place that must know the complete, current
roster of every action that will ever exist — the opposite of the extension seam `ITaskAction` is
meant to be. `TaskActions.cs`'s own summary already states the goal: "the one place both
`CallbackRouter` and a future button-rendering caller can read" — a catalogue *other* code reads,
not one `CallbackRouter` re-enumerates every time an action is added. F11-4 adds no new action (its
presets all resolve through `ScheduleAction`), but the next feature that adds a genuinely new
`ITaskAction` would have to edit `CallbackRouter.HandleAsync` too, just to teach it which edit that
action deserves — a second class needing a change for every new implementation, when only the DI
registration should need one.

*Rejected: a display-intent flag on `TaskActionDefinition`* (a `RendersAsCompleted` bool, say).
This looks more open than the switch — a new action sets its own flag rather than teaching the
router about itself — but it duplicates, less reliably, what the task's own `Status` already
carries first-hand. `TaskActionDefinition` is declared once, statically, per *action* —
`TaskActions.Schedule` is one record, shared by every `+1h` tap that will ever happen. Whether one
particular tap produced a completed task is a fact about *that call*, not the button:
`DoneAction.ExecuteAsync` on an already-completed task returns a *failure*, never reaching this
branch, and a hypothetical future action could complete a task under some conditions and not
others. A static flag cannot express "sometimes this, sometimes that" — it would either be wrong
for one case, or degenerate into the router reading the flag *and* the status anyway, the exact
duplication this alternative was meant to avoid.

**The chosen shape, and why it is neither of those.** `result.Value!.Status` is a fact already
computed, by the single writer, about the exact call that just happened — `RescheduleAsync` and
`CompleteAsync` both return the row they just wrote (F11-1, F11-2), so `CallbackRouter` reads a
value already reflecting every rule `TaskService` enforces, including ones this slice never has to
know about. A third action later that sometimes completes a task needs no change here: whatever
`Status` comes back decides the edit, automatically. This is the literal meaning of `CallbackRouter`
naming neither `DoneAction` nor `ScheduleAction` anywhere in its body — it names
`ReminderStatus.Completed`, a fact about a task, never an action's key or type.

### 7. The too-old-message guard is re-examined, not merely preserved, and `CallbackRouter`
   moves to the four-argument `TryDecode` overload

**Decision:** The `messageText is not null` guard now wraps only the call to
`MarkCompletedTaskAsync`; the call to `UpdateTaskAsync` is unconditional. `CallbackRouter.cs:85`'s
call becomes `CallbackCodec.TryDecode(data, out var actionKey, out var taskId, out var argument)`,
and `argument` — not a literal `string.Empty` — is passed to `ExecuteAsync`.

**Why the guard's scope changes, argued from what each edit actually needs.** `Message.Text` is
bound with a plain `var`, not a null-checked pattern, because Telegram omits it once a message is
judged too old to still carry content. `MarkCompletedTaskAsync` genuinely needs that text: it wraps
it in `<s>...</s>`, and there is nothing to wrap if Telegram sent nothing. `UpdateTaskAsync` needs
no such thing — its text comes entirely from `task.ToMessageText(clock)`, built fresh from the row
`RescheduleAsync` just returned, never reading `Message.Text`. Keeping the guard around
`UpdateTaskAsync` too would silently drop a `+1h` tap's edit on any reminder old enough to have lost
its text — precisely the reminders most likely to be tapped, since an old message is one already
sitting in chat history waiting for exactly this action.
`Listener_ScheduleTappedOnATooOldMessage_StillUpdatesTheMessage` is the assertion that proves this
distinction holds, not merely states it.

**Why the four-argument overload now, correcting F11-2's own deferral.** F11-2's Decision 4 kept
`CallbackRouter` on the three-argument overload, passing `string.Empty` unconditionally, reasoning
that decoding a real argument with nowhere for it to go would be untestable plumbing. `ScheduleAction`
is now that somewhere: `Listener_OwnerTapsSchedule_...` depends on the real `"+1h"` argument
reaching it, and would regress to "always refused as unrecognised" if `CallbackRouter` still
hardcoded `string.Empty` — exactly the assertion that forces this change red before it is made.

**The reply-text mapping for the new `ErrorCode`.** `ErrorCode.TaskActionArgumentUnrecognized`
reuses the existing `ThatButtonIsNoLongerValid` constant, rather than introducing a fourth toast
string:

```csharp
var reply = result switch
{
    { IsSuccess: true } => null,
    { Error: ErrorCode.TaskAlreadyCompleted } => AlreadyDone,
    { Error: ErrorCode.TaskActionArgumentUnrecognized } => ThatButtonIsNoLongerValid,
    _ => CouldNotFindThatTask,
};
```

An argument this action does not recognise is, from the tapper's point of view, indistinguishable
from any other reason a button no longer does anything sensible — a stale button from a future
release, or a corrupted callback string, both already map to the same sentence. No test demands a
distinct message, and inventing one on spec would be exactly the kind of speculative text `AGENTS.md`'s
YAGNI posture rules out.

**No `CallbackRouterTests` scenario for "Schedule tapped on an already-completed task."**
`RescheduleAsync` refusing `ErrorCode.TaskAlreadyCompleted` is already proven at the `TaskService`
level (F11-1's `RescheduleAsync_TaskAlreadyCompleted_IsRejectedAndDueTimeUnchanged`); the
router-level mapping to `AlreadyDone` with no edit is already proven, through a real tap, by
`Listener_DoneTappedOnAnAlreadyCompletedTask_AnswersAlreadyDoneWithoutEditingAgain` — and that
mapping does not care which action produced the error, which is Decision 6's whole point. A second
test differing only in which button was tapped would duplicate both facts at once, which
`AGENTS.md`'s anti-duplication rule is written broadly enough to rule out here too.

### 8. Documentation: what is corrected here, and why §3.4/§3.6 are left stale

**Decision:** Spec §6.4's button table and its `EditAction`-costs-an-LLM-call sentence, spec §4.2's
snooze/reschedule bullet, the backlog's F11 entry, and the backlog's `DeliveryAttempts` row are all
corrected in this commit. Spec §3.4 (the `Impl` folder tree) and §3.6 (the extension-seam table)
are read but left uncorrected.

**Why now, when F11-1 and F11-2 both declined.** Both prior slices gave the identical reason for
leaving these sections alone: each sentence they would need to correct describes a button, a
rendered message, or a real-phone-verifiable behaviour, and neither slice made any of those real.
This slice does, for the first time. Correcting the prose now, in the commit that makes it true, is
the positive case `AGENTS.md`'s "correct it in the same commit" rule describes.

**§6.4's corrected table drops `Snooze 1h`, `Tomorrow`, and `Edit` and adds one row, `+1h` /
`ScheduleAction`.** The three dropped actions were never built under those names at all — F11-1's
own "How this slice fits" already renamed the family to `ScheduleAction` before any of it existed —
so this is the first opportunity to say what exists, not a report of what F11-3 changed. The
`EditAction`-costs-an-LLM-call sentence is removed outright: no action here costs an LLM call, and
editing a task's title through a follow-up message is not tracked anywhere in the backlog under any
name — said plainly rather than dropped with no trace.

**Why §3.4 and §3.6 are left stale — a decision, stronger than "the Actions row is ambiguous."**
Both sections are a whole-slice architecture sketch, written before any of slice 1 existed, already
diverged from reality on axes this slice has nothing to do with (see "Verified facts"): §3.4 places
`MessageHandler` under `Services/` when it lives under `Telegram/`, and names an `AgentService` that
exists nowhere in this repository's history; §3.6 lists four `IAssistantTool` implementations where
exactly one, `CreateTaskTool`, is registered. No prior slice has ever gone back to correct either
section as its own piece became real — these sections are read as the original whole-slice vision,
not a currency-tracked description of today's state. Spot-fixing only the Actions row now, while
`AgentService` and the tool roster sit equally wrong nearby, would imply a completeness the rest of
the section lacks. A full resync is legitimate work, but not this slice's — folding it in here would
be scope creep past what `AGENTS.md`'s rule actually asks: correcting a decision *this change*
alters, not auditing a document's entire currency because one part happens to be adjacent.

---

## What this slice does NOT include

- **`Schedule` becoming a menu opener, and the `+3h` / `Tonight 20:00` / `Tomorrow 09:00` / `Back`
  presets.** All **F11-4**'s. `ScheduleAction`'s single `argument == PlusOneHour` check (Decision 2)
  is deliberately not a lookup table — F11-4 is what turns it into one, once there is more than one
  recognised value to look up.
- **Any `ILocalTimeResolver` use inside `ScheduleAction`.** F11-4's, for the wall-clock presets
  (`Tonight 20:00`, `Tomorrow 09:00`) that need one — see Decision 2.
- **Keyboard variation in `CallbackRouter`** — every re-render still attaches the same two-button
  keyboard `SendTaskAsync` would build. A keyboard that varies by state (for example, a `Back`
  button only inside a submenu) is **F11-4**'s.
- **Storing a capture message's own id on `reminder_tasks`, and a fired reminder deleting an
  earlier message.** Both **F10-5**'s, per the backlog entry that already names them; nothing in
  this slice touches message deletion at all.
- **Closing GitHub issue #27.** Mentioned in Decision 2 and the corrected backlog entry, not
  closed: the issue also covers listing undated tasks, which nothing here adds.
- **A full resync of spec §3.4/§3.6 against the current repository layout.** Explicitly considered
  and declined — see Decision 8.
- **A new project reference, a new NuGet package, a new WireMock stub mapping, or any
  `Assistant.Repository`/migration change.** Confirmed by "Verified facts" — every endpoint this
  slice calls, `sendMessage` and `editMessageText`, is already stubbed.

---

## File Structure

```
src/Assistant.Contracts/
    ErrorCode.cs                                        + TaskActionArgumentUnrecognized (appended)
    TaskActions.cs                                       + Schedule; All = [Done, Schedule]

src/Assistant.Interfaces/
    INotifier.cs                                          + UpdateTaskAsync

src/Assistant.Impl/
    Services/Actions/ScheduleAction.cs                    new file
    Mapping/ReminderTaskMappingExtensions.cs              + ToMessageText
    Telegram/TelegramNotifier.cs                          SendTaskAsync rebuilt on a shared
                                                           two-button keyboard; + UpdateTaskAsync
    Telegram/CallbackRouter.cs                            renders by Status; 4-arg TryDecode;
                                                           + ILocalTimeResolver dependency
    Telegram/MessageHandler.cs                            reply built via ToMessageText -- its
                                                           only change
    ImplServiceCollectionExtensions.cs                    + ScheduleAction registration

tests/Assistant.IntegrationTests/
    Jobs/DueReminderJobTests.cs                           1 Fact renamed and strengthened
    Telegram/TelegramListenerTests.cs                     2 Facts strengthened, 1 renamed
    Telegram/CallbackRouterTests.cs                       + FakeTimeProvider; + 3 Facts
    Telegram/TelegramNotifierTests.cs                     + 1 Fact

docs/
    design/slice-1-reminders.md                           §6.4, §4.2 corrected
    design/2026-08-22-slice-1-feature-backlog.md          F11 and F13 entries, DeliveryAttempts row
    e2e-local.md                                          +1h real-phone verification paragraph
```

No `Assistant.Models`, `Assistant.Repository`, `Assistant.Worker`, migration file,
`CallbackCodec.cs`, `ITaskAction.cs`, `ITaskService.cs`, `TaskService.cs`, `DoneAction.cs`,
`CallbackCodecTests.cs`, `TaskServiceTests.cs`, `TaskActionsTests.cs`, or `CallbackRouterTests.cs`'s
existing scenarios is touched beyond what is listed above.

---

## Validation

**Test count arithmetic.** Baseline, run directly against this exact `HEAD` (see "Verified facts"):
59 unit, 74 integration.

- Unit: no unit test file is touched by this slice. `TaskActionsTests.cs`'s two existing facts
  (`All_EveryDeclaredKey_IsUnique`, `All_EveryDeclaredKey_ContainsNoColon`) both generalise over
  `TaskActions.All` and so automatically extend to cover `Schedule` with no code change. 59 + 0 =
  **59**.
- Integration: `CallbackRouterTests.cs` gains 3 new `[Fact]`s (8 → 11); `TelegramNotifierTests.cs`
  gains 1 (5 → 6); `DueReminderJobTests.cs` renames and strengthens 1 existing fact (5 → 5, no
  count change); `TelegramListenerTests.cs` strengthens 2 existing facts, renaming 1 (13 → 13, no
  count change). No other integration test file changes. 74 + 3 + 1 = **78**.

**Expected final state: 59 unit, 78 integration — already confirmed, not merely predicted.** The
isolated worktree built during this planning session reached exactly this state:

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

**No WireMock stub change and no `docker compose ... --build` are required.** Every request this
slice's new code makes — a `sendMessage` with a two-button keyboard, an `editMessageText` for
`UpdateTaskAsync` — hits a mapping `tests/Assistant.WireMock/TelegramStubs.cs` already installs,
matched by HTTP path alone (see "Verified facts"). Confirmed directly: the full integration suite,
including all four new/changed test files, ran green against the already-running, entirely
unmodified `personal-ai-assistant-wiremock-1` container. If the
`personal-ai-assistant-postgres-test-1`/`personal-ai-assistant-wiremock-1` containers are not
already running, `docker compose -f compose.test.yaml up -d` (no `--build`) is sufficient.

---
## Steps

**Decisions this slice carries:** all eight, above. **Consumes:** `RescheduleAsync` and
`CallbackCodec`'s fourth segment (F11-1); `ITaskAction`'s widened signature and
`ITaskService.CompleteAsync`'s widened return (F11-2). **Produces:** everything in "File
Structure."

**Why one commit.** The moment `CallbackRouter` changes at all it needs `ScheduleAction`
registered, `TaskActions.Schedule` to decode against, `INotifier.UpdateTaskAsync` to call, and
`ToMessageText` to build a message with — landing any proper subset leaves either dead code or a
build that does not compile. The steps below are each their own compiler- or test-forced
increment, verified individually in an isolated worktree, but they land in one commit — the same
shape F11-2's own "true atomic step" finding took, scaled up.

### Commit 1: the button appears

**Files:**
- Modify: `src/Assistant.Contracts/ErrorCode.cs`, `src/Assistant.Contracts/TaskActions.cs`
- Add: `src/Assistant.Impl/Services/Actions/ScheduleAction.cs`
- Modify: `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`
- Modify: `src/Assistant.Impl/Telegram/TelegramNotifier.cs`
- Modify: `src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs`
- Modify: `src/Assistant.Impl/Telegram/MessageHandler.cs`
- Modify: `src/Assistant.Interfaces/INotifier.cs`
- Modify: `src/Assistant.Impl/Telegram/CallbackRouter.cs`
- Modify: `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs`
- Modify: `tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs`
- Modify: `docs/design/slice-1-reminders.md`, `docs/design/2026-08-22-slice-1-feature-backlog.md`, `docs/e2e-local.md`

- [ ] **Step 1: `TaskActions.Schedule`, `ScheduleAction`, and its DI registration — driven red
  by the existing catalogue test, with no new test written**

Append `TaskActionArgumentUnrecognized` to `ErrorCode.cs` (after `ModelNamedUnknownTool`), and add
`Schedule` to `TaskActions.cs`:

```diff
 public static class TaskActions
 {
     public static TaskActionDefinition Done { get; } = new(...);
+
+    /// <summary>The Schedule button's definition.</summary>
+    /// <remarks>
+    /// The label is "+1h", not "Schedule": there is no menu yet for a "Schedule" label to name.
+    /// F11-4 renames it once tapping the button opens a menu instead of applying one preset.
+    /// </remarks>
+    public static TaskActionDefinition Schedule { get; } = new(
+        Key: "schedule",
+        Label: "+1h",
+        Description: "Moves the task's due time to one hour from now, arming a reminder for the "
+            + "first time on a task that had none. Refused when the task is already complete, or "
+            + "when the callback carries an argument other than \"+1h\".");

-    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done];
+    public static IReadOnlyList<TaskActionDefinition> All { get; } = [Done, Schedule];
 }
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~ITaskAction_RegisteredImplementations"
```

Expected: builds clean, but the existing `ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly`
(F6-2, unmodified) fails — captured directly from this planning session's own isolated worktree:

```
Assert.Equal() Failure: Collections differ
Expected: ["done", "schedule"]
Actual:   ["done"]
```

`TaskActions.All` now has two keys; nothing is registered under `"schedule"` yet. Add
`src/Assistant.Impl/Services/Actions/ScheduleAction.cs` in full:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;
using Assistant.Models;

namespace Assistant.Impl.Services.Actions;

/// <summary>
/// Moves a task's due time forward in response to its Schedule button being tapped.
/// </summary>
/// <param name="taskService">The single writer for tasks.</param>
/// <param name="timeProvider">
/// The current instant. <see cref="PlusOneHour"/> is added to this, never to the task's own
/// <see cref="ReminderTask.DueAt"/> -- see <see cref="ExecuteAsync"/>.
/// </param>
internal sealed class ScheduleAction(ITaskService taskService, TimeProvider timeProvider) : ITaskAction
{
    /// <summary>
    /// The only argument this action understands.
    /// </summary>
    internal const string PlusOneHour = "+1h";

    /// <inheritdoc/>
    public TaskActionDefinition Definition => TaskActions.Schedule;

    /// <inheritdoc/>
    /// <remarks>
    /// Only <see cref="PlusOneHour"/> is understood; any other <c>argument</c> -- a future preset
    /// this slice does not yet implement, or a corrupted callback string -- is refused with
    /// <see cref="ErrorCode.TaskActionArgumentUnrecognized"/> rather than guessed at.
    /// <para>
    /// One hour is added to <see cref="TimeProvider"/>'s current instant, never to the task's own
    /// <see cref="ReminderTask.DueAt"/>: a reminder that has already fired carries a due time in
    /// the past, and adding an hour to a past instant would still be in the past, or would fire
    /// again immediately -- neither of which is "one hour from now". Adding to the current UTC
    /// instant, rather than to a local wall-clock reading of it, also means this stays correct
    /// across a daylight-saving transition, where a wall-clock "+1 hour" can be zero or two real
    /// hours away. This is why this action needs no <see cref="ILocalTimeResolver"/>: it never
    /// reads or writes a wall-clock reading at all.
    /// </para>
    /// </remarks>
    public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
        argument == PlusOneHour
            ? taskService.RescheduleAsync(taskId, timeProvider.GetUtcNow().AddHours(1), ct)
            : Task.FromResult(Result<ReminderTask>.Failure(ErrorCode.TaskActionArgumentUnrecognized));
}
```

Register it in `ImplServiceCollectionExtensions.AddAssistantListener`, beside `DoneAction`:

```diff
         services.AddScoped<ITaskAction, DoneAction>();
+        services.AddScoped<ITaskAction, ScheduleAction>();
         services.AddHostedService<TelegramListener>();
```

(Also update that method's own `<remarks>` to note `ScheduleAction` shares `AddAssistantServices`'s
`TimeProvider`, and that `CallbackRouter` will need `AddAssistantTime`'s `ILocalTimeResolver` too,
once Step 4 adds it.)

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~ITaskAction_RegisteredImplementations"
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` The catalogue test now passes. The full
suite is unaffected everywhere else — **74 passed**, unchanged (nothing yet calls `ScheduleAction`
from a real tap, and `SendTaskAsync` still renders one button).

- [ ] **Step 2: `TelegramNotifier`'s two-button keyboard — red on the three hard-coded-one-button
  tests, fixed forward in the same step**

Replace `SendTaskAsync`'s body and add a shared keyboard builder:

```diff
-    public async Task SendTaskAsync(Guid taskId, string text, CancellationToken ct)
-    {
-        var keyboard = new InlineKeyboardMarkup(
-            InlineKeyboardButton.WithCallbackData(TaskActions.Done.Label,
-                CallbackCodec.Encode(TaskActions.Done.Key, taskId)));
+    public async Task SendTaskAsync(Guid taskId, string text, CancellationToken ct) =>
         await bot.SendMessage(
-            settings.OwnerChatId, Escape(text), ParseMode.Html, replyMarkup: keyboard, cancellationToken: ct);
-    }
+            settings.OwnerChatId, Escape(text), ParseMode.Html,
+            replyMarkup: BuildTaskKeyboard(taskId), cancellationToken: ct);
+
+    private static InlineKeyboardMarkup BuildTaskKeyboard(Guid taskId) => new(
+        new[]
+        {
+            InlineKeyboardButton.WithCallbackData(
+                TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, taskId)),
+            InlineKeyboardButton.WithCallbackData(
+                TaskActions.Schedule.Label,
+                CallbackCodec.Encode(TaskActions.Schedule.Key, taskId, ScheduleAction.PlusOneHour)),
+        });
```

(`SendTaskAsync`'s own `<remarks>` are rewritten too, per Decision 5 — see the full text there.)

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: builds clean; exactly three tests fail, all the same shape (captured directly):

```
DueReminderJobTests.RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask [FAIL]
TelegramListenerTests.Listener_OwnerSendsAMessageWithNoDueTime_RepliesThatNoReminderWillFire [FAIL]
TelegramListenerTests.Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton [FAIL]
  Assert.Single() Failure: The collection contained 2 items
Failed!  - Failed: 3, Passed: 71, Skipped: 0, Total: 74
```

Fix all three forward. `DueReminderJobTests.cs` — rename and re-assert both buttons:

```diff
-    public async Task RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask()
+    public async Task RunAsync_TaskIsDue_AttachesTheDoneAndScheduleButtonsForThatTask()
     {
         ...
-        var row = Assert.Single(sent.ReplyMarkup!.InlineKeyboard);
-        var button = Assert.Single(row);
-        Assert.Equal(TaskActions.Done.Label, button.Text);
-        Assert.Equal(CallbackCodec.Encode(TaskActions.Done.Key, task.Id), button.CallbackData);
+        var expectedRow = new[]
+        {
+            new InlineButtonPayload(TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, task.Id)),
+            new InlineButtonPayload(
+                TaskActions.Schedule.Label, CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h")),
+        };
+        Assert.Equivalent(expectedRow, Assert.Single(sent.ReplyMarkup!.InlineKeyboard), strict: true);
     }
```

`TelegramListenerTests.cs` — the same reshaping for
`Listener_OwnerSendsAMessageWithADueTime_...` (renamed to
`...AndRepliesWithTheDueTimeAndTheDoneAndScheduleButtons`, its `<see cref>` in the stranger test
updated to match), and for `Listener_OwnerSendsAMessageWithNoDueTime_...`:

```diff
         var row = Assert.Single(sent[0].ReplyMarkup!.InlineKeyboard);
-        var button = Assert.Single(row);
-        Assert.Equal(TaskActions.Done.Label, button.Text);
+        Assert.Equal(2, row.Count);
+        Assert.Equal(TaskActions.Done.Label, row[0].Text);
+        Assert.Equal(TaskActions.Schedule.Label, row[1].Text);
     }
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **74 passed** — unchanged total, all three
now proving two buttons.

- [ ] **Step 3: extract `ToMessageText` — a pure refactor, no new test; the existing suite is
  the safety net**

```diff
 public static class ReminderTaskMappingExtensions
 {
+    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";
+
     public static ReminderTask ToModel(...) => new() { ... };
+
+    /// <summary>Renders the message text a task's own reminder message should carry.</summary>
+    /// <param name="task">The task to render.</param>
+    /// <param name="clock">Converts a stored due instant back to the configured local zone.</param>
+    /// <returns>
+    /// "<c>{title} -- due {local due time}.</c>" when <see cref="ReminderTask.DueAt"/> is set, or
+    /// "<c>{title} -- saved with no reminder.</c>" otherwise.
+    /// </returns>
+    /// <remarks>
+    /// The shared shape both <c>MessageHandler</c> and <c>CallbackRouter</c> render, so the two
+    /// can never silently drift onto two different sentences for the same task state.
+    /// </remarks>
+    public static string ToMessageText(this ReminderTask task, ILocalTimeResolver clock) =>
+        task.DueAt is { } dueAt
+            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
+            : $"{task.Title} -- saved with no reminder.";
 }
```

In `MessageHandler.cs` — this file's only change:

```diff
-using System.Globalization;
 using Assistant.Contracts;
+using Assistant.Impl.Mapping;
 ...
-    private const string DueTimeFormat = "dddd d MMMM yyyy, HH:mm";
-
     private const string Unreachable = ...
 ...
         var task = outcome.Value!;
-        var reply = task.DueAt is { } dueAt
-            ? $"{task.Title} -- due {clock.ToLocal(dueAt).ToString(DueTimeFormat, CultureInfo.InvariantCulture)}."
-            : $"{task.Title} -- saved with no reminder.";
-        await notifier.SendTaskAsync(task.Id, reply, ct);
+        await notifier.SendTaskAsync(task.Id, task.ToMessageText(clock), ct);
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded.` **59 unit / 74 integration passed** — both unchanged, proving the
extraction produces byte-identical text (`TelegramListenerTests.cs`'s two capture-reply tests
already pin the exact string on both sides).

- [ ] **Step 4: write the three new `CallbackRouterTests` scenarios — red against the still-old
  router; then `INotifier.UpdateTaskAsync`, `TelegramNotifier`'s implementation, and
  `CallbackRouter`'s rewrite to turn them green**

Register a `FakeTimeProvider` in `CallbackRouterTests.InitializeAsync` (after
`AddAssistantServices()`): `services.AddSingleton<TimeProvider>(new FakeTimeProvider(AsOf));` — so
"one hour from now" is a fixed, assertable instant. Insert three new facts before
`ITaskAction_RegisteredImplementations_MatchTheCatalogueKeysExactly`:

```csharp
    /// <summary>
    /// When the owner taps Schedule on a task
    /// Then its due time becomes exactly one hour from now, not one hour from its old due time
    /// And its reminder-sent marker is cleared
    /// And the message is edited in place with the new due time and both buttons still attached
    /// And the callback query is answered with no toast.
    /// </summary>
    [Fact]
    public async Task Listener_OwnerTapsSchedule_MovesTheDueTimeOneHourFromNowAndUpdatesTheMessage()
    {
        // Arrange
        var task = BuildReminderTask(dueAt: AsOf.AddDays(-1), reminderSentAt: AsOf.AddDays(-1));
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h");
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        Assert.Equivalent(new AnswerCallbackQueryPayload(CallbackQueryId, null), Assert.Single(answered), strict: true);

        var expectedEdit = new EditMessageTextPayload(
            OwnerChatId, MessageId, "call the bank -- due Tuesday 25 August 2026, 16:00.", "Html",
            new ReplyMarkupPayload(
            [
                [
                    new InlineButtonPayload(TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, task.Id)),
                    new InlineButtonPayload(
                        TaskActions.Schedule.Label, CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h")),
                ],
            ]));
        Assert.Equivalent(expectedEdit, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(AsOf.AddHours(1), stored!.DueAt);
        Assert.Null(stored.ReminderSentAt);
    }

    /// <summary>
    /// When Schedule is tapped with an argument this action does not recognise
    /// Then the callback query is answered that the button is no longer valid
    /// And nothing is edited
    /// And the task's due time is unchanged.
    /// </summary>
    [Fact]
    public async Task Listener_ScheduleTappedWithAnUnrecognisedArgument_AnswersButLeavesTheDueTimeUnchanged()
    {
        // Arrange
        var originalDueAt = AsOf.AddHours(-2);
        var task = BuildReminderTask(dueAt: originalDueAt);
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "tomorrow");
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, task.Title, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        var expectedAnswer = new AnswerCallbackQueryPayload(CallbackQueryId, "That button is no longer valid.");
        Assert.Equivalent(expectedAnswer, Assert.Single(answered), strict: true);
        Assert.Empty(await wireMock.EditedMessagesAsync());

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(originalDueAt, stored!.DueAt);
    }

    /// <summary>
    /// When the owner taps Schedule on a reminder whose message is too old for Telegram to carry its text
    /// Then the task's due time still moves, even from having none at all
    /// And the message is still edited, since rebuilding it fresh needs no previous text
    /// And the callback query is answered.
    /// </summary>
    [Fact]
    public async Task Listener_ScheduleTappedOnATooOldMessage_StillUpdatesTheMessage()
    {
        // Arrange
        var task = BuildReminderTask();
        await postgres.SaveAsync(task);
        var data = CallbackCodec.Encode(TaskActions.Schedule.Key, task.Id, "+1h");
        await wireMock.SeedCallbackQueryUpdatesAsync(
            new InboundCallbackQuery(10, CallbackQueryId, OwnerChatId, MessageId, null, data));

        // Act
        await _sut.StartAsync(CancellationToken.None);

        // Assert
        var answered = await wireMock.WaitForAnsweredCallbacksAsync(1, AnswerDeadline);
        Assert.Equal(CallbackQueryId, Assert.Single(answered).CallbackQueryId);
        Assert.Single(await wireMock.EditedMessagesAsync());

        var stored = await _repository.FindAsync(task.Id, CancellationToken.None);
        Assert.Equal(AsOf.AddHours(1), stored!.DueAt);
    }
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~Listener_OwnerTapsSchedule|FullyQualifiedName~Listener_ScheduleTapped"
```

Expected: builds clean (nothing here calls `INotifier` directly); all three fail against the
still-unmodified router, every one refused as `TaskActionArgumentUnrecognized` because
`CallbackRouter` still hardcodes `string.Empty` (F11-2) — captured directly:

```
Listener_OwnerTapsSchedule_...  Expected: null              Actual: "I could not find that task."
Listener_ScheduleTapped...Unrecognised...  Expected: "That button is no longer valid."  Actual: "I could not find that task."
Listener_ScheduleTappedOnATooOldMessage_...  Assert.Single() Failure: The collection was empty
Failed!  - Failed: 3, Passed: 0, Skipped: 0, Total: 3
```

Now implement. Append to `INotifier.cs`:

```csharp
    /// <summary>
    /// Updates a previously sent task message to reflect a change other than completion -- a new
    /// due time, for example -- keeping its task keyboard attached.
    /// </summary>
    /// <param name="messageId">Identifier of the message to edit.</param>
    /// <param name="taskId">
    /// The task the message announces. The adapter needs this to rebuild a channel-neutral handle
    /// for each action it re-attaches -- it never sees any other part of a database shape.
    /// </param>
    /// <param name="text">
    /// The message body, as plain text. The adapter escapes whatever its channel requires
    /// before sending, so callers must not pre-escape.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the edit has been accepted.</returns>
    /// <remarks>
    /// Distinct from <see cref="MarkCompletedTaskAsync"/>, which clears the keyboard: the task
    /// this message announces is not finished, only some other field of it changed, so the same
    /// actions it could already accept must remain tappable. The keyboard this attaches is built
    /// the same way <see cref="SendTaskAsync"/>'s own is -- from <paramref name="taskId"/> alone,
    /// never from a caller-supplied keyboard -- so both methods stay in agreement as new actions
    /// are added.
    /// </remarks>
    Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct);
```

```bash
dotnet build --no-restore
```

Expected: fails with exactly this error, captured directly:

```
TelegramNotifier.cs(28,93): error CS0535: 'TelegramNotifier' does not implement interface member 'INotifier.UpdateTaskAsync(int, Guid, string, CancellationToken)'
```

Implement it, beside `MarkCompletedTaskAsync`:

```csharp
    /// <inheritdoc/>
    /// <remarks>
    /// Re-attaches the same two-button keyboard <see cref="SendTaskAsync"/> would build fresh for
    /// <paramref name="taskId"/> -- the task this message announces is not finished, so whatever
    /// it could already accept a tap on, it must still accept a tap on.
    /// </remarks>
    public async Task UpdateTaskAsync(int messageId, Guid taskId, string text, CancellationToken ct) =>
        await bot.EditMessageText(
            settings.OwnerChatId, messageId, Escape(text), ParseMode.Html,
            BuildTaskKeyboard(taskId), cancellationToken: ct);
```

Replace `src/Assistant.Impl/Telegram/CallbackRouter.cs` in full:

```csharp
using Assistant.Contracts;
using Assistant.Impl.Mapping;
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
/// <param name="notifier">Where a successful action's edit is delivered.</param>
/// <param name="actions">
/// Every registered task action, resolved by matching <see cref="TaskActionDefinition.Key"/>
/// against each one's <see cref="ITaskAction.Definition"/>.
/// </param>
/// <param name="clock">Renders a stored due instant back in the configured local zone.</param>
/// <remarks>
/// The callback query is answered last in every branch, after any edit a successful action
/// triggers, never before. The sole exception is the first guard's bare early return, unreachable
/// in practice since <see cref="TelegramListener.DispatchAsync"/> only invokes handlers whose
/// <see cref="Handles"/> matches the update's own type.
/// <para>
/// Which edit a successful action gets is decided by the resulting task's own
/// <see cref="ReminderTask.Status"/>, never by which action ran: a
/// <see cref="ReminderStatus.Completed"/> task strikes through and loses its keyboard via
/// <see cref="INotifier.MarkCompletedTaskAsync"/>; any other status re-renders in place via
/// <see cref="INotifier.UpdateTaskAsync"/>, text rebuilt fresh from the task. This method names
/// neither <c>DoneAction</c> nor <c>ScheduleAction</c> anywhere in its body -- the task's own
/// status decides, so a future action needs no change here.
/// </para>
/// <para>
/// <c>Message.Text</c> is bound with a plain <c>var</c> because Telegram omits it once a message
/// is judged too old to still carry content. This guards only
/// <see cref="INotifier.MarkCompletedTaskAsync"/>, which needs the prior text to strike through;
/// <see cref="INotifier.UpdateTaskAsync"/> builds its text fresh from the task and runs
/// unconditionally.
/// </para>
/// <para>
/// The owner check lives inline here, the same as <see cref="MessageHandler"/>'s own remarks
/// explain. Unlike <see cref="MessageHandler"/>, a non-owner's tap is still answered, per spec 6.4,
/// but the action itself never runs and nothing is edited.
/// </para>
/// </remarks>
internal sealed class CallbackRouter(
    TelegramSettings settings,
    ITelegramBotClient bot,
    INotifier notifier,
    IEnumerable<ITaskAction> actions,
    ILocalTimeResolver clock) : ITelegramUpdateHandler
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

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **77 passed** — the 74-test baseline plus
the three new Schedule scenarios.

- [ ] **Step 5: `TelegramNotifierTests`'s new escaping test**

```csharp
    /// <summary>
    /// When a task message is updated
    /// And its title contains "&amp;", "&lt;" and "&gt;"
    /// Then the edit escapes all three in order and re-attaches the Done and Schedule buttons.
    /// </summary>
    /// <remarks>
    /// The expected string was worked out by hand, the same discipline
    /// <see cref="SendAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder"/> uses.
    /// The expected keyboard is asserted only because <c>strict: true</c> compares the whole
    /// payload -- this test's own subject is escaping, not the keyboard, which
    /// <c>CallbackRouterTests</c> already proves through a real tap.
    /// </remarks>
    [Fact]
    public async Task UpdateTaskAsync_TextContainsAngleBracketsAndAmpersand_EscapesAllThreeInOrder()
    {
        // Arrange
        const int messageId = 42;
        var taskId = Guid.NewGuid();
        const string text = "Meet R&D <at 5> & confirm";
        var expected = new EditMessageTextPayload(
            OwnerChatId, messageId, "Meet R&amp;D &lt;at 5&gt; &amp; confirm", "Html",
            new ReplyMarkupPayload(
            [
                [
                    new InlineButtonPayload(TaskActions.Done.Label, CallbackCodec.Encode(TaskActions.Done.Key, taskId)),
                    new InlineButtonPayload(
                        TaskActions.Schedule.Label, CallbackCodec.Encode(TaskActions.Schedule.Key, taskId, "+1h")),
                ],
            ]));

        // Act
        await _sut.UpdateTaskAsync(messageId, taskId, text, CancellationToken.None);

        // Assert
        Assert.Equivalent(expected, Assert.Single(await wireMock.EditedMessagesAsync()), strict: true);
    }
```

(Add `using Assistant.Contracts;` and `using Assistant.Impl.Telegram;` to this file's imports.)

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded.` **59 unit / 78 integration passed** — matching "Validation" exactly.

- [ ] **Step 6: the documentation corrections**

Spec §4.2's rules list — the stale snooze/reschedule bullet:

```diff
-- Snooze and reschedule will clear `ReminderSentAt` and reset `DeliveryAttempts`... (F11); today only `MarkReminderSentAsync` sets `ReminderSentAt`...
-- Snooze or reschedule on a completed task is rejected.
+- `RescheduleAsync` (F11) sets `DueAt` and clears `ReminderSentAt`, so the task fires again, including a task that had no `DueAt` at all. It does not reset `DeliveryAttempts`: that column does not exist yet and returns at F13, not F11 as this section once said. **Pairing `DueAt` with `ReminderSentAt` is the reason a single writer is mandatory.**
+- Rescheduling a completed task is rejected.
```

Spec §6.4's button table — drop `Snooze 1h`/`Tomorrow`/`Edit`, add `+1h`:

```diff
 | `Done` | `DoneAction` | ... |
-| `Snooze 1h` | `SnoozeAction` (arg `1h`) | ... |
-| `Tomorrow` | `RescheduleAction` (arg `tomorrow`) | ... |
-| `Edit` | `EditAction` | ... |
-
-`EditAction` is the only one that costs an LLM call, and only on the follow-up message.
+| `+1h` | `ScheduleAction` (arg `+1h`) | `RescheduleAsync` to one hour from now; clears `ReminderSentAt` so it fires again |
+
+As shipped at F11-3: two buttons, one action beyond `Done`. `+1h` stands alone rather than opening
+a menu -- F11-4 turns it into one. No action costs an LLM call; editing a task's title or notes
+through a follow-up message (`EditAction`) was dropped from an earlier design with no replacement
+-- it is not scheduled anywhere in this backlog.
```

Backlog's F11 and F13 entries, and the `DeliveryAttempts` deferred-property row (`F11` -> `F13`) --
full replacement text given in Decision 8 and reproduced verbatim in the shipped commit.

`docs/e2e-local.md`, after the existing Done-button verification paragraph:

```diff
 message) -- and it is F6's own `observable` requirement (backlog §1).
 
+The reminder also carries a `+1h` button next to Done. Tap it: the message updates in place with a
+new due time roughly an hour ahead, and both buttons remain attached -- proof that
+`UpdateTaskAsync`'s re-attached keyboard reached the app, not just the stub's request log. As
+above, this step is the owner's own to run -- no agent may run the worker against real Telegram --
+and it is F11-3's own observable milestone.
+
 ## Troubleshooting
```

- [ ] **Step 7: run the whole suite, then commit**

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **59 passed** unit. **78 passed**
integration — matching "Validation" exactly.

```bash
git add src/Assistant.Contracts/ErrorCode.cs src/Assistant.Contracts/TaskActions.cs \
        src/Assistant.Impl/Services/Actions/ScheduleAction.cs \
        src/Assistant.Impl/ImplServiceCollectionExtensions.cs \
        src/Assistant.Impl/Telegram/TelegramNotifier.cs \
        src/Assistant.Impl/Mapping/ReminderTaskMappingExtensions.cs \
        src/Assistant.Impl/Telegram/MessageHandler.cs \
        src/Assistant.Interfaces/INotifier.cs src/Assistant.Impl/Telegram/CallbackRouter.cs \
        tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramListenerTests.cs \
        tests/Assistant.IntegrationTests/Telegram/CallbackRouterTests.cs \
        tests/Assistant.IntegrationTests/Telegram/TelegramNotifierTests.cs \
        docs/design/slice-1-reminders.md docs/design/2026-08-22-slice-1-feature-backlog.md \
        docs/e2e-local.md
git commit
```

Message:

```
feat: F11-3 -- the button appears

Every task message now carries a second button, +1h, backed by a new
ScheduleAction that calls RescheduleAsync -- the first real caller for
plumbing F11-1 and F11-2 built with none. CallbackRouter now renders a
successful tap by the task's own resulting Status, never by which
action ran: Completed strikes through as before; anything else
re-renders in place via a new INotifier.UpdateTaskAsync, with text
rebuilt through a ToMessageText renderer extracted from MessageHandler
now that CallbackRouter is its second caller.

+1h adds one hour to the current UTC instant, never to the task's own
due time and never through a local wall-clock reading -- the first
keeps rescheduling an already-fired reminder in the future instead of
immediate; the second keeps it correct across daylight saving. It also
works on a task with no due time at all, the real fix for the gap
GitHub issue #27 named, though nothing here closes that issue.

TelegramNotifier builds both buttons by hand in one row, verified
against the installed Telegram.Bot 22.10.2.1 metadata rather than
assumed. CallbackRouter moves to CallbackCodec's four-argument
TryDecode overload and re-examines its message-too-old guard: it still
protects MarkCompletedTaskAsync, which needs prior text to strike
through, but not UpdateTaskAsync, which needs none.

Every documentation correction deferred since F11-1 lands here too.

Tests: 59 unit (unchanged), 78 integration, up from 74. No WireMock
stub change and no docker rebuild were needed. Build clean, zero
warnings.

Plan: docs/plans/2026-09-07-f11-3-the-button-appears.md

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `TaskActions.Schedule`'s label is `"+1h"`, declared before `All`, which becomes
      `[Done, Schedule]` (Decision 1)
- [ ] `ScheduleAction.ExecuteAsync` adds one hour to `TimeProvider.GetUtcNow()`, never to
      `task.DueAt`, and takes no `ILocalTimeResolver`; an argument other than `"+1h"` is refused
      with the appended `ErrorCode.TaskActionArgumentUnrecognized`; no `ScheduleActionTests.cs`
      unit test file exists (Decision 2)
- [ ] `ToMessageText` is a `ReminderTask` extension method in `ReminderTaskMappingExtensions.cs`,
      not a new type; `MessageHandler.cs`'s only change is calling it (Decision 3)
- [ ] `INotifier.UpdateTaskAsync` takes `(int messageId, Guid taskId, string text,
      CancellationToken ct)` — plain text, a channel-neutral id, never a model or a pre-built
      keyboard (Decision 4)
- [ ] `TelegramNotifier` builds both buttons by hand, in one row, via the
      `IEnumerable<InlineKeyboardButton>` overload verified against the installed Telegram.Bot
      metadata, not assumed (Decision 5)
- [ ] `CallbackRouter.HandleAsync` names neither `DoneAction` nor `ScheduleAction`, nor any other
      action, anywhere in its body — it branches only on `result.Value!.Status` (Decision 6)
- [ ] The `messageText is not null` guard wraps only `MarkCompletedTaskAsync`; `UpdateTaskAsync` is
      unconditional. `CallbackRouter.cs`'s `TryDecode` call is the four-argument overload, and the
      decoded `argument` — not `string.Empty` — reaches `ExecuteAsync`; no `CallbackRouterTests`
      scenario duplicates "Schedule on a completed task" (Decision 7)
- [ ] Spec §6.4, §4.2, and the backlog's F11/F13/`DeliveryAttempts` entries are corrected; spec
      §3.4/§3.6 are read and deliberately left unmodified (Decision 8)
- [ ] `ITaskService.cs`, `TaskService.cs`, `CallbackCodec.cs`, `ITaskAction.cs`, `DoneAction.cs`,
      `TaskServiceTests.cs`, `CallbackCodecTests.cs`, `TaskActionsTests.cs`, and every migration
      file are unchanged ("File Structure")
- [ ] Exactly three new `[Fact]`s in `CallbackRouterTests.cs` (8 → 11), one in
      `TelegramNotifierTests.cs` (5 → 6); `DueReminderJobTests.cs` and `TelegramListenerTests.cs`
      keep their existing counts, with facts renamed and strengthened, not added
- [ ] Every new or changed public member carries a three-line-tag `<summary>` plus every
      `<param>`/`<returns>` `CS1591`/`CS1573` requires; every new test's summary is Gherkin, one
      clause per line
- [ ] No emoji anywhere, including the commit message and the new documentation paragraphs
- [ ] **No plan-internal decision citation inside any C# code block, doc comment, or commit
      message** — every fenced code block above was re-read for this before the plan was finalised
- [ ] Plain ASCII `--` in every C# comment and doc-file edit and the commit message body; this
      document's own markdown prose uses real em dashes
- [ ] No `docker compose ... --build` or migration is claimed necessary (confirmed in "Verified
      facts")
- [ ] Build reaches `0 Warning(s)` throughout every step above — confirmed empirically at each
      checkpoint, not only at the end

**Whole feature (F11), once F11-4 also lands:**
- [ ] This slice's diff measures 498 lines; the running total across F11's four PRs is not itself
      a budget — each is independently under the 1000-line budget regardless of the eventual sum.
- [ ] `ScheduleAction`'s single-value argument check (Decision 2), `TelegramNotifier`'s hand-built
      keyboard (Decision 5), and `CallbackRouter`'s status-based rendering (Decision 6) are all
      shaped so F11-4 extends them — a lookup table of presets, a variable-width row, a `Back`
      button — without a second look at anything this slice already proved correct.
- [ ] The documentation corrected here (Decision 8) needs one more pass at F11-4, when
      `+3h`/`Tonight 20:00`/`Tomorrow 09:00`/`Back` make `Schedule`'s own label true again.
