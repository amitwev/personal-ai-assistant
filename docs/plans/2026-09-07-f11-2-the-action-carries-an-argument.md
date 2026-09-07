# F11-2 — the action carries an argument

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F11-1 (commit `8989d6c`) gave `ITaskService` a `RescheduleAsync` method and gave
`CallbackCodec` the ability to carry a callback button's optional fourth argument segment —
plumbing nothing outside its own tests called yet. This slice is the second of F11's four pull
requests: it widens `ITaskAction.ExecuteAsync` to accept that argument and to return the task it
acted on, widens `ITaskService.CompleteAsync` to the same returning shape `RescheduleAsync`
already has, and updates `DoneAction` and `CallbackRouter` only as far as compiling the widened
interface requires. No button changes shape, no message changes text, and no new capability is
observable from a real chat — the same posture F11-1 held for its own two pieces of plumbing.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **no new NuGet package** and **no database migration**: nothing about `reminder_tasks`
changes, and no new dependency is needed to widen an existing method's signature.

**Spec:** `docs/design/slice-1-reminders.md` §4.2 (`TaskService` as single writer — "the interface
grows a method per feature that needs one," read here as also covering widening an existing
method for a feature the whole interface must speak); §6.4 (inline buttons and the callback wire
format — still describing the pre-F11 `Snooze 1h` / `Tomorrow` / `Edit` design, read for context,
not corrected here — see Decision 7); §7.2 (unit vs. integration split); §7.3 (assertion
standard); §12.1 (XML docs); §12.5 (primary constructors); §12.6 (no emoji).

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — the F11 entry (currently line
633, `**F11 · Snooze and reschedule**`) is read for context but not edited, for the same reason
F11-1 left it alone: it still describes a `SnoozeAction`/`RescheduleAction`/`EditAction` design
this project has already moved on from in practice (`ScheduleAction`, per F11-1's own "How this
slice fits"), and no slice before the button actually ships should touch it.

---

## How this slice fits

F11 splits into four independently reviewable pull requests, in a strict dependency order forced
by what compiles against what:

1. **F11-1 (merged, `8989d6c`) — the writer moves the clock.** `ITaskService.RescheduleAsync` and
   `CallbackCodec`'s fourth segment, plus their own tests. Nothing outside those tests calls
   either yet.
2. **F11-2 (this plan) — the action learns to carry an argument.** `ITaskAction.ExecuteAsync`
   gains the `argument` parameter and returns the task; `ITaskService.CompleteAsync` widens to
   match; `DoneAction` and `CallbackRouter` are updated only as far as compiling requires. Still
   no button.
3. **F11-3 — the button appears.** `TaskActions.Schedule` and `ScheduleAction` (calling
   `RescheduleAsync`); `INotifier.UpdateTaskAsync` and `TelegramNotifier`'s two-button keyboard;
   `CallbackRouter`'s switch to status-based rendering; the shared `ToMessageText` renderer
   (deferred here to there — see Decision 5); the DI registration; the tests that hard-code one
   button; every documentation correction.
4. **F11-4 — the menu.** `Schedule` becomes a menu opener; `+3h`, `Tonight 20:00`,
   `Tomorrow 09:00`, and `Back` join `+1h` as presets.

**Why this slice measures so much smaller than F11-1's.** F11-1 introduced two genuinely new
capabilities (`RescheduleAsync`, `CallbackCodec`'s fourth segment), each proven correct by four
and two new tests respectively. This slice adds no new capability at all — it widens two existing
signatures to shapes F11-1 already established as correct (`Result<ReminderTask>`, matching
`RescheduleAsync`), and follows the widening through the two files the compiler forces. There is
nothing new to prove; there is only "does the existing behaviour still hold," which the existing
test suite already answers.

**The measured total, drafted in full, built, and tested green — not estimated.** Every line below
was produced by actually writing this slice's six files in an isolated `git worktree` branched
from this repository's own `HEAD` (`8989d6c`, F11-1's own merge commit — `main` and this feature
branch share the same tree at that point), then reading `git diff --numstat` against it directly:

| File | + | - |
| :--- | ---: | ---: |
| `src/Assistant.Interfaces/ITaskAction.cs` | 11 | 4 |
| `src/Assistant.Interfaces/ITaskService.cs` | 6 | 3 |
| `src/Assistant.Impl/Services/TaskService.cs` | 4 | 4 |
| `src/Assistant.Impl/Services/Actions/DoneAction.cs` | 2 | 1 |
| `src/Assistant.Impl/Telegram/CallbackRouter.cs` | 1 | 1 |
| **Code subtotal** | **24** | **13** |
| `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs` | 5 | 2 |
| **Test subtotal** | **5** | **2** |
| **Grand total** | **29** | **15** |

**Total changed lines: 44** (29 insertions + 15 deletions), 956 lines under the 1000-line budget.
No documentation file is touched — see "What this slice does NOT include."

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** Not exercised by new code this
  slice writes — `TaskService`, `DoneAction`, and `CallbackRouter` all already use one, and no
  separate constructor is introduced anywhere.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- No emoji anywhere: source, tests, docs, or commit messages.
- **Never run `docker compose down -v`.** This slice adds no WireMock stub mapping and no new
  test infrastructure, so no image rebuild is needed: the existing
  `personal-ai-assistant-postgres-test-1` / `personal-ai-assistant-wiremock-1` containers,
  already running, are sufficient. If nothing is running, `docker compose -f compose.test.yaml up
  -d` (no `--build`) is enough.
- PR budget: 1000 changed lines per PR, excluding the plan document. This slice measures at 44
  lines — far under budget.
- Plain ASCII `--` in every C# file; real em dashes in this document's own markdown prose.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was checked directly against the working tree at `8989d6c` (`HEAD` of
`feature/f11-2-the-action-carries-an-argument`) or produced by a command actually run during this
planning session.

- **`ITaskAction.ExecuteAsync`'s only production call sites are `DoneAction.cs:16-17` (the sole
  implementation) and `CallbackRouter.cs:99` (the sole caller)** — confirmed exhaustively by
  `grep -rn "ExecuteAsync" src/ tests/ --include="*.cs"`. No test file references
  `ITaskAction.ExecuteAsync` directly: there is no `DoneActionTests.cs` anywhere under `tests/`,
  and `DoneAction`'s behaviour is exercised only through `CallbackRouterTests.cs`, at the
  integration level.
- **`ITaskAction.cs`'s existing `<remarks>` (lines 12-13) reads: "`DoneAction` is the first
  implementation; snooze, reschedule and edit actions follow at F11, each adding one more
  implementation rather than changing this one."** This is the sentence the brief for this slice
  calls "wrong on both counts": the actual next action is `ScheduleAction` (F11-1's own "How this
  slice fits," not `SnoozeAction`/`RescheduleAction`/`EditAction`), and this very slice changes
  `ExecuteAsync`'s signature rather than leaving it fixed. Corrected in Decision 1.
- **`ITaskService.CompleteAsync` returns bare `Task<Result>` today** (`ITaskService.cs:54`),
  while `RescheduleAsync`, added at F11-1, already returns `Task<Result<ReminderTask>>`
  (`ITaskService.cs:99`) — the exact shape this slice's widened `CompleteAsync` copies.
  `TaskService.CompleteAsync`'s implementation (`TaskService.cs:49-71`) is already structurally
  identical to `RescheduleAsync`'s (`:84-105`): find, refuse-if-null, refuse-if-completed, mutate,
  save, return success. No new `ErrorCode` member is needed — `TaskNotFound` and
  `TaskAlreadyCompleted` (`ErrorCode.cs:16`, `:52`) already cover both refusals.
- **`DoneAction.ExecuteAsync` is a single expression-bodied member that forwards
  `taskService.CompleteAsync(taskId, ct)` directly as its own return value** (`DoneAction.cs:16-17`
  today). This is the fact Decision 6 is built on: it means `CompleteAsync`'s return type cannot
  widen without `DoneAction`'s own declared return type changing in the same step, which in turn
  forces `ITaskAction`'s interface to widen in that same step too, unless a throwaway unwrapping
  shim is introduced — rejected in Decision 6.
- **`CallbackRouter.cs:99`'s only use of `ExecuteAsync`'s result is `result.IsSuccess`
  (`:101`) and a `switch` expression pattern-matching `IsSuccess`/`Error` (`:106-111`)** — both
  members exist identically on `Result<ReminderTask>` as on bare `Result`, so widening `result`'s
  inferred type requires no change to either usage. `result.Value` is read nowhere in
  `CallbackRouter.cs`.
- **`CallbackCodec`'s three- and four-argument `TryDecode` overloads both already exist**
  (`CallbackCodec.cs:54-55`, `:71-103`, from F11-1). `CallbackRouter.cs:85` calls the
  three-argument overload today; nothing in this slice's scope requires that call to change (see
  Decision 4).
- **`CallbackRouterTests.cs` has exactly 8 test cases** (6 `[Fact]` methods, one `[Theory]` with
  2 `[InlineData]` rows, and one non-async `[Fact]`) — confirmed by
  `dotnet test --filter "FullyQualifiedName~CallbackRouterTests"` both before and after this
  slice's own draft implementation: **8 passed** in both cases, with no line of that file changed.
- **`MessageHandler.cs` builds its reply text inline at lines 151-154**, not "roughly 150-153" —
  `var task = outcome.Value!;` through the ternary building `reply`. `MessageHandler.HandleAsync`
  calls `tool.ExecuteAsync(toolCall.ArgumentsJson, ct)` at line 133, which is
  `IAssistantTool.ExecuteAsync` (`IAssistantTool.cs:55`) — an entirely different interface from
  `ITaskAction.ExecuteAsync`. `MessageHandler.cs` has zero coupling to anything this slice
  touches, and is therefore untouched regardless of the `ToMessageText` question (Decision 5).
- **No `.editorconfig` exists anywhere in this repository** (confirmed by
  `find . -iname ".editorconfig"`), and `Directory.Build.props`'s only analyzer-adjacent setting
  is `EnforceCodeStyleInBuild=true`, with no severity overrides. Confirmed empirically, not
  assumed: `DoneAction`'s new, deliberately-unused `argument` parameter built with
  `0 Warning(s)` in this plan's own verified draft, below.
- **`tests/Assistant.UnitTests` has no mocking library and no hand-rolled fake anywhere** —
  confirmed by reading `Assistant.UnitTests.csproj`'s package references (no Moq, no
  NSubstitute) and by `find tests/Assistant.UnitTests -iname "Fake*"` (no results). Every class
  with a real dependency (`TaskService`, `DoneAction`, `CallbackRouter`) is tested at the
  integration level instead. This is the fact Decision 3 rests on.
- **Baseline test counts, run directly against this exact `HEAD`, not assumed.**
  `dotnet build --no-restore` — `Build succeeded. 0 Warning(s). 0 Error(s).` `dotnet test
  tests/Assistant.UnitTests --no-build` — **59 passed**, 0 failed. Against the already-running
  `personal-ai-assistant-postgres-test-1` and `personal-ai-assistant-wiremock-1` containers,
  `dotnet test tests/Assistant.IntegrationTests --no-build` — **74 passed**, 0 failed.
- **This plan's own draft was built and tested green, not merely written.** All six files below
  were applied inside an isolated `git worktree` branched from this repository's own `HEAD`
  (never touching this working tree's `src/` or `tests/`), following the exact
  red/green sequence given in "Steps," and reached: `Build succeeded. 0 Warning(s). 0 Error(s).`,
  **59 unit / 74 integration passed** — an unchanged count, confirmed deliberate (see
  "Validation"). `CallbackRouterTests.cs` was additionally re-run in isolation against the fully
  implemented draft (**8 passed**, matching its own untouched baseline exactly), and the one
  modified `TaskServiceTests.cs` fact was re-run three more times in isolation
  (`dotnet test --filter "FullyQualifiedName~CompleteAsync_TaskWasPending"`) with no failure. The
  worktree and its branch were then removed.

---

## Inherited context: what this slice reads from earlier features

`ITaskAction` and `DoneAction`'s existing shape (F6-1/F6-2) are read and widened, not replaced.
`ITaskService`'s "grows a method per feature that needs one" posture (F5a, F6-1, F10-1, F11-1) is
extended to cover widening an existing method, not only adding new ones. `RescheduleAsync`'s
return shape (F11-1) is the direct template `CompleteAsync`'s own widening copies verbatim.
`CallbackCodec`'s two `TryDecode` overloads (F11-1) are read but neither is switched by
`CallbackRouter` in this slice (Decision 4). `CallbackRouter`'s existing single-call-site
structure (F6-2/F6-3) is read and touched at exactly one line.

---

## Decisions

### 1. `ITaskAction.ExecuteAsync` is modified in place — not a second interface

**Decision:**

```csharp
Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct);
```

The existing `ITaskAction` interface is widened. No `IArgumentTaskAction` or other second
interface is introduced for actions that carry an argument.

**Why, said out loud — this project modifies an existing interface rather than inventing a
speculative abstraction.** `CallbackRouter` resolves every action through one
`IEnumerable<ITaskAction> actions` collection, matched by `Definition.Key`
(`CallbackRouter.cs:91`). A second interface for argument-carrying actions would force that
lookup to either search two collections or require every future action to implement a shared
base type anyway — strictly more machinery than one interface whose every implementation accepts
the same three parameters and is free to ignore what it does not need, exactly as `DoneAction`
does with `argument` here. `ITaskService`'s own remarks already state the house rule this follows:
"the interface grows a method per feature that needs one" — the natural extension of that rule to
an interface with a single behavioural method is that the method itself grows a parameter when
every implementation needs to speak the new shape to one shared caller.

**The corrected remarks.** The old sentence — "snooze, reschedule and edit actions follow at F11,
each adding one more implementation rather than changing this one" — is wrong on both counts this
slice's own brief identified: the actual next action is `ScheduleAction`, not three separately
named ones, and this interface changes rather than staying fixed. The corrected text (see Step 2)
states plainly that `ExecuteAsync` gained `argument` and a widened return here, at F11-2, and
that this was a deliberate in-place modification, not an oversight the original sentence failed
to anticipate.

### 2. `ITaskService.CompleteAsync` widens to `Task<Result<ReminderTask>>`, matching
   `RescheduleAsync`'s shape exactly

**Decision:**

```csharp
Task<Result<ReminderTask>> CompleteAsync(Guid id, CancellationToken ct);
```

Refused with the same `ErrorCode.TaskNotFound`/`TaskAlreadyCompleted` members it already uses; no
enum member is added. `TaskService.CompleteAsync`'s implementation changes only its three
`return` statements, from `Result.Failure(...)`/`Result.Success()` to
`Result<ReminderTask>.Failure(...)`/`Result<ReminderTask>.Success(task)` — the method's own logic
(find, refuse, mutate, save) does not change at all.

**Why this shape, not left bare.** `DoneAction`'s eventual caller, `CallbackRouter`, is one step
removed from needing the completed task's own fields today — its rendering does not change this
slice (Decision 4) — but `CompleteAsync` returning the task it just completed, rather than a bare
success, is the same argument `RescheduleAsync` already settled at F11-1: a caller acts on the row
just written without a second read. Widening it now, proven correct by its own test, means
whichever F11-3 caller eventually reads `.Value` — most plausibly `CallbackRouter`'s own
`ToMessageText` rendering, once that exists — inherits an already-correct method rather than one
whose widening is bundled into the same commit that first reads the new field.

**Why the existing failure-path tests need no change, and the success-path test does.**
`CompleteAsync_TaskAlreadyCompleted_IsRejectedAndCompletedAtUnchanged` and
`CompleteAsync_TaskDoesNotExist_IsRejected` both declare their result with `var` and only ever
read `.Error` — both compile and pass unmodified against the widened return type, and neither is
touched. `CompleteAsync_TaskWasPending_SetsStatusAndStampsCompletedAt`, by contrast, is the one
place able to prove the new capability (that a successful call now hands back the completed task
itself) — leaving it unchanged would mean nothing in the suite distinguishes a correct
`Result<ReminderTask>.Success(task)` from a bug that returned `Result<ReminderTask>.Success(null!)`
or a stale copy. It is strengthened, not left alone, and renamed to
`CompleteAsync_TaskWasPending_SetsStatusAndStampsCompletedAtAndReturnsIt` to say what it now
proves — mirroring `RescheduleAsync_ReminderAlreadySent_SetsTheNewDueTimeAndClearsReminderSentAt`,
which already asserts on both `result.Value` and the persisted row.

### 3. `DoneAction` follows both signature changes and gets no new test of its own

**Decision:**

```csharp
public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
    taskService.CompleteAsync(taskId, ct);
```

`argument` is accepted and ignored. No `DoneActionTests.cs` is added.

**Why no test is warranted, argued rather than assumed.** Three independent reasons converge.
First, `DoneAction`'s entire externally observable behaviour — completing a pending task, refusing
an already-completed one, refusing a missing one — is already fully covered by
`CallbackRouterTests.cs` at the integration level, and by the widened `TaskServiceTests.cs` at the
service level; adding a unit test that re-asserts the same outcomes through a different route
would violate `AGENTS.md`'s own rule: "Do not... write a unit test for behaviour an integration
test already covers." Second, this codebase has no mocking library and no hand-rolled fake
anywhere (see "Verified facts") — a `DoneActionTests.cs` would need to invent that pattern from
nothing, for a class whose own logic is one line. Third, the only genuinely new fact introduced
here — that `argument` is accepted but ignored — has no test that could observe it doing anything
else: `CallbackRouter` passes `string.Empty` unconditionally this slice (Decision 4), so there is
no code path anywhere that could hand `DoneAction` a non-empty argument for a test to prove is
still ignored. A test asserting "this unused parameter is unused" would test the compiler, not a
business rule.

### 4. `CallbackRouter` passes `string.Empty`, and keeps calling the three-argument `TryDecode`
   overload — the smallest honest change the compiler forces

**Decision:**

```csharp
var result = await action.ExecuteAsync(taskId, string.Empty, ct);
```

`CallbackRouter.cs:85`'s existing call to the three-argument `TryDecode` overload is **not**
changed to the four-argument one.

**The choice, argued.** Two shapes were available. The rejected one: switch line 85 to
`CallbackCodec.TryDecode(data, out var actionKey, out var taskId, out var argument)` and pass the
decoded `argument` through to `ExecuteAsync`. The chosen one: keep line 85 exactly as it is, and
pass a literal `string.Empty` to `ExecuteAsync` instead. Both compile; both leave
`CallbackRouterTests.cs` passing unmodified (verified directly — see "Verified facts"), because no
existing test encodes a four-segment callback string, so the two decode paths produce identical
results for every input either exercises.

The deciding argument is what each choice would leave behind. `DoneAction`, the only registered
action today, ignores its argument entirely (Decision 3). Decoding a real argument from callback
data, only to hand it to an action that discards it, is speculative plumbing with no test able to
force it and no consumer able to observe it — exactly the shape `AGENTS.md`'s aggressive YAGNI
posture rules out. Passing `string.Empty` is the honest statement of the current truth: no
registered action needs an argument yet. `TryDecode`'s four-argument overload exists
(from F11-1) precisely so that F11-3's `CallbackRouter`, once `ScheduleAction` exists to consume
a real decoded argument, can switch to it in the same commit that gives the argument somewhere to
go — proven correct by a test that can actually observe the difference, rather than threaded
through now on faith.

### 5. The `ToMessageText` renderer is deferred to F11-3; `MessageHandler.cs` is untouched here

**Decision:** No renderer is extracted from `MessageHandler` in this slice. `MessageHandler.cs`
has zero lines changed.

**The tension, stated rather than glossed over.** F11-1's own "How this slice fits" projected this
extraction as part of F11-2's scope, on the same footing as `RescheduleAsync`: a piece of shared
plumbing built and proven before its second caller exists, the posture F10-1 first established for
`ITaskService.CreateAsync`. This slice's own brief explicitly reopens that placement and permits
moving it — and on inspection, the case for extracting a rendering rule now is meaningfully weaker
than the case for building `RescheduleAsync` or `CallbackCodec`'s fourth segment ahead of their
callers, for three reasons.

First, `RescheduleAsync` and the codec's overloads are each *new capability*, provable correct by
a dedicated test that exercises a rule nothing else in the suite already checks. A `ToMessageText`
extraction is *code motion*: the exact string it would produce is already fully pinned down by
`TelegramListenerTests.cs`'s existing assertions on `MessageHandler`'s reply text
(`Listener_OwnerSendsAMessageWithADueTime_...`, `Listener_OwnerSendsAMessageWithNoDueTime_...`).
Moving that logic into a shared member changes where the code lives, not what it proves.

Second, `AGENTS.md`'s own rule — "do not write a unit test for behaviour an integration test
already covers" — means an extracted `ToMessageText` could not honestly earn a new unit test of
its own yet: the two branches (a due instant set, or not) are already fully characterised by the
existing integration suite. Without a new test, extracting it now produces a diff that changes
`MessageHandler.cs` for zero test benefit and zero behavioural change — a refactor with no
reviewable claim beyond "this compiles and nothing moved."

Third, and decisively: the entire value an extraction offers — preventing two call sites from
silently drifting apart on what should be identical rendering — does not exist until the second
call site does. Today there is exactly one caller. Building the seam now cannot be justified by
"F11-3 will need it," because F11-3 is also the first point at which the *shape* the seam must
take becomes checkable by anything other than reading the code: only once `CallbackRouter` has an
actual second call to make can a test assert that both callers produce the same string. Extracting
early risks nothing catching a subtly wrong shape until F11-3 anyway, which means the "prove it
correct before it is needed" argument that justified `RescheduleAsync` does not transfer here.

**Conclusion:** this belongs in F11-3, at the commit that gives it a second caller and, with it,
the only test that could ever prove the extraction faithful. `MessageHandler.cs` is entirely
outside this slice's scope — not because nothing about it changed in the drafted worktree (it
did not, independent of this decision — see "Verified facts"), but because there was never
anything for this slice to extract it *for*.

### 6. The true atomic step is five production files, not three — and no smaller honest split
   exists

**The brief's own framing, and where it undercounts.** F11-2's brief states that changing
`ITaskAction.ExecuteAsync`'s signature forces "the interface, `DoneAction`, and `CallbackRouter`"
to compile together as one atomic step. That is correct as far as it goes, but it is not the
whole coupling. `DoneAction.ExecuteAsync` is a single expression-bodied member that forwards
`taskService.CompleteAsync(taskId, ct)` directly as its own return value (see "Verified facts").
Widening `ITaskService.CompleteAsync`'s return type — Decision 2, needed regardless of `ITaskAction`
— breaks that same forwarding expression, because `Task<Result<ReminderTask>>` is not implicitly
convertible to the (unwidened) `Task<Result>` the old interface declares. **`DoneAction`'s own
compile depends on both changes simultaneously,** which means `ITaskService.CompleteAsync`'s
interface and implementation belong inside the same atomic step as `ITaskAction`, not staged
before it.

**The alternative considered and rejected: a throwaway unwrapping shim.** `CompleteAsync` could
widen alone, first, if `DoneAction` were temporarily rewritten to unwrap the extra value it does
not yet have anywhere to put:

```csharp
public async Task<Result> ExecuteAsync(Guid taskId, CancellationToken ct)
{
    var result = await taskService.CompleteAsync(taskId, ct);
    return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!.Value);
}
```

This would let `CompleteAsync`'s widening land as its own standalone, test-first step, mirroring
`RescheduleAsync`'s treatment at F11-1. It is rejected because the very next step immediately
deletes it again, once `ITaskAction` also widens and `DoneAction` reverts to a plain one-line
forward — two edits to the same file, the second of which exists solely to undo the first, with
no state in between that is itself worth reviewing or worth its own commit. That is not a smaller
honest step; it is the same amount of work with an extra, admittedly temporary detour, which
reads worse under review than one clearly-labelled atomic step, and edges toward exactly the kind
of "placeholder implementation" this project's conventions rule out.

**The actual smallest honest step, then, is:** `ITaskService.CompleteAsync` (interface and
implementation), `ITaskAction.ExecuteAsync` (interface), `DoneAction` (implementation), and
`CallbackRouter`'s one call site — five files, in one step, preceded by writing the one test able
to force it red: the strengthened `CompleteAsync_TaskWasPending_...` fact from Decision 2, which
fails to compile against the current bare `Result` return before any production file changes.
Nothing else in this slice has a test of its own to write first (Decisions 3 and 4 both conclude
no new test is warranted), so this one red test is what drives the entire atomic bundle.

### 7. No documentation is touched

**Decision:** Spec §6.4, spec §4.2, and the backlog's F11 entry are read for context but not
edited.

**Why — reusing F11-1's own Decision 5 argument, because the reasoning is identical.** Every
sentence those sections would need corrected describes behaviour visible from a button, a
rendered message, or a real-phone verification — none of which exists after this slice either: no
button, no renderer, no observable change to any reply. `ITaskAction.ExecuteAsync` and
`ITaskService.CompleteAsync` both still have exactly the same production caller they had before
this slice (`CallbackRouter`, via `DoneAction`), reachable the same way, producing the same
answers. Editing spec or backlog prose now would describe a capability two method signatures have
gained with nothing new a real interaction can do about it — the drift `AGENTS.md`'s "correct it
in the same commit" rule exists to prevent, not invite. The correction lands at F11-3, the slice
that makes a second action, and its argument, real.

---

## What this slice does NOT include

- **`ErrorCode.TaskActionArgumentUnrecognized`, `TaskActions.Schedule`, `ScheduleAction` itself,
  `INotifier.UpdateTaskAsync`, `TelegramNotifier`'s two-button keyboard, `CallbackRouter`'s switch
  to status-based rendering, the DI registration, and the three existing tests
  (`DueReminderJobTests.RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask`, and
  `TelegramListenerTests.Listener_OwnerSendsAMessageWithADueTime_StoresItAndRepliesWithTheDueTimeAndADoneButton`
  / `Listener_OwnerSendsAMessageWithNoDueTime_RepliesThatNoReminderWillFire`) that assert exactly
  one button.** All **F11-3**'s. All three of those tests still pass unmodified after this slice
  — confirmed directly, since nothing here changes how many buttons render or what any reply
  says.
- **The shared `ToMessageText` renderer, and any change to `MessageHandler.cs`.** Deferred to
  **F11-3** — see Decision 5. `MessageHandler.cs` has zero lines changed in this slice.
- **Any documentation correction** — spec §6.4's button table, spec §4.2's bullet, the backlog's
  F11 entry, the `DeliveryAttempts` row, and `docs/e2e-local.md`. All **F11-3**'s — see Decision
  7.
- **`CallbackRouter`'s switch to the four-argument `TryDecode` overload.** F11-3's, once
  `ScheduleAction` gives a decoded argument somewhere real to go — see Decision 4.
- **The `[Schedule]` button becoming a menu, and the `+3h`/`Tonight 20:00`/`Tomorrow 09:00`/`Back`
  presets.** **F11-4**'s.
- **A new project reference, a new NuGet package, a new WireMock stub mapping, or any
  `Assistant.Repository`/migration change.** Confirmed by "Verified facts" — this slice widens two
  existing method signatures and nothing else.
- **A `DoneActionTests.cs` unit test file.** See Decision 3.

---

## File Structure

```
src/Assistant.Interfaces/
    ITaskAction.cs                                       ExecuteAsync gains argument, widened
                                                          return; remarks corrected
    ITaskService.cs                                      CompleteAsync widened return

src/Assistant.Impl/
    Services/TaskService.cs                              CompleteAsync impl widened
    Services/Actions/DoneAction.cs                        follows both signature changes
    Telegram/CallbackRouter.cs                            passes string.Empty as the new argument

tests/Assistant.IntegrationTests/
    Services/TaskServiceTests.cs                          1 Fact renamed and strengthened
```

No `Assistant.Models`, `Assistant.Contracts`, `Assistant.Repository`, `Assistant.Worker`,
migration file, `MessageHandler.cs`, `CallbackCodec.cs`, `TaskActions.cs`, `ErrorCode.cs`,
`INotifier.cs`, `TelegramNotifier.cs`, `CallbackRouterTests.cs`, `CallbackCodecTests.cs`,
`TelegramListenerTests.cs`, `DueReminderJobTests.cs`,
`ImplServiceCollectionExtensions.cs`, or documentation file is touched.

---

## Validation

**Test count arithmetic.** Baseline, run directly against this exact `HEAD` (see "Verified
facts"): 59 unit, 74 integration.

- Unit: no unit test file is touched by this slice at all — `CallbackCodecTests.cs`, the
  `Architecture/` suite, and every other unit test file are unaffected. 59 + 0 = **59**.
- Integration: `TaskServiceTests.cs` renames one existing `[Fact]` and strengthens its assertions
  — no `[Fact]` is added or removed. `CallbackRouterTests.cs` is unmodified, confirmed by
  re-running it in isolation against the fully implemented draft (8 passed, unchanged). No other
  integration test file changes. 74 + 0 = **74**.

**Why zero new tests is the right count for this slice, not an oversight.** This slice adds no
new observable capability: it widens two existing signatures to shapes already proven correct
elsewhere (`RescheduleAsync`, at F11-1), and the one behavioural fact newly worth proving —
`CompleteAsync` now hands back the task it completed — is proven by strengthening an existing
`[Fact]` rather than adding one, the same accounting convention F11-1 used for a renamed
`CallbackCodecTests` fact.

**Expected final state: 59 unit, 74 integration — already confirmed, not merely predicted.** The
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
Passed!  - Failed: 0, Passed: 74, Skipped: 0, Total: 74 - Assistant.IntegrationTests.dll
```

No `docker compose ... --build` is required — this slice adds no WireMock stub mapping. If the
`personal-ai-assistant-postgres-test-1`/`personal-ai-assistant-wiremock-1` containers are not
already running, `docker compose -f compose.test.yaml up -d` (no `--build`) is sufficient.

---

## Steps

**Decisions this slice carries:** all seven, given in full above.

**Consumes:** `RescheduleAsync`'s return shape (F11-1), `ITaskAction`/`DoneAction`'s existing
shape (F6-1/F6-2), `CallbackRouter`'s existing single-call-site structure (F6-2/F6-3).
**Produces:** `ITaskAction.ExecuteAsync`'s widened signature; `ITaskService.CompleteAsync`'s
widened return; `TaskService.CompleteAsync`'s widened implementation; `DoneAction`'s and
`CallbackRouter`'s compile-forced follow-through; one strengthened integration test.

**Why one commit, and why it cannot be split further.** Decision 6 established that
`ITaskService.CompleteAsync` (interface and implementation), `ITaskAction.ExecuteAsync`
(interface), `DoneAction`, and `CallbackRouter` must all change together: `DoneAction`'s direct
forwarding of `taskService.CompleteAsync(...)` couples the two interfaces' widenings into a single
compile unit, and the only alternative — a throwaway unwrapping shim in `DoneAction`, written and
then immediately deleted — was rejected as manufactured work with no independent review value.
The one test able to drive this bundle red is Decision 2's strengthened `CompleteAsync` fact,
written first.

### Commit 1: the action learns to carry an argument

**Files:**
- Modify: `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`
- Modify: `src/Assistant.Interfaces/ITaskAction.cs`
- Modify: `src/Assistant.Interfaces/ITaskService.cs`
- Modify: `src/Assistant.Impl/Services/TaskService.cs`
- Modify: `src/Assistant.Impl/Services/Actions/DoneAction.cs`
- Modify: `src/Assistant.Impl/Telegram/CallbackRouter.cs`

- [ ] **Step 1: Strengthen the one test this slice needs — watch it fail to compile**

In `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`, replace the existing
`CompleteAsync_TaskWasPending_SetsStatusAndStampsCompletedAt` test in full:

```csharp
    /// <summary>
    /// When a pending task is completed
    /// Then its status becomes Completed
    /// And its CompletedAt is no longer null
    /// And the completed task is handed back to the caller.
    /// </summary>
    [Fact]
    public async Task CompleteAsync_TaskWasPending_SetsStatusAndStampsCompletedAtAndReturnsIt()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: AsOf.AddHours(-1));
        await postgres.SaveAsync(reminderTask);

        // Act
        var result = await _sut.CompleteAsync(reminderTask.Id, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(ReminderStatus.Completed, result.Value!.Status);
        Assert.NotNull(result.Value.CompletedAt);
        var stored = await _repository.FindAsync(reminderTask.Id, CancellationToken.None);
        Assert.Equal(ReminderStatus.Completed, stored!.Status);
        Assert.NotNull(stored.CompletedAt);
    }
```

```bash
dotnet build --no-restore
```

Expected: the build **fails** with exactly these two errors (captured directly from this
planning session's own isolated worktree, at this precise state):

```
TaskServiceTests.cs(115,55): error CS1061: 'Result' does not contain a definition for 'Value' and no accessible extension method 'Value' accepting a first argument of type 'Result' could be found (are you missing a using directive or an assembly reference?)
TaskServiceTests.cs(116,31): error CS1061: 'Result' does not contain a definition for 'Value' and no accessible extension method 'Value' accepting a first argument of type 'Result' could be found (are you missing a using directive or an assembly reference?)

2 Error(s)
```

Both errors name the same fact: `ITaskService.CompleteAsync` still returns bare `Result`, with no
`Value` to read. Step 2 fixes this and everything it forces along with it (Decision 6).

- [ ] **Step 2: Widen `CompleteAsync`, widen `ITaskAction`, and follow both through
  `DoneAction` and `CallbackRouter` — watch it pass**

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
/// first implementation; <c>ScheduleAction</c> follows at F11-3, reading <c>argument</c> as a
/// preset key such as <c>+1h</c>. This interface is modified in place as each new capability
/// demands one: <see cref="ExecuteAsync"/> gained <c>argument</c> and a widened return here, at
/// F11-2, rather than a second interface being invented alongside it for actions that carry one.
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
    /// The button's carried argument, decoded from the callback data's optional fourth segment,
    /// or an empty string when the action takes none. <c>DoneAction</c> ignores it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task, or the reason the action was refused.</returns>
    Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct);
}
```

In `src/Assistant.Interfaces/ITaskService.cs`, replace `CompleteAsync`'s doc comment and
signature:

```diff
     /// <summary>
     /// Marks a task as completed.
     /// </summary>
     /// <param name="id">The task to complete.</param>
     /// <param name="ct">Cancellation token.</param>
     /// <returns>
-    /// Success, or the reason it was refused. Refused when no task carries the identifier, or
-    /// when the task has already been completed.
+    /// The completed task, or the reason it was refused: no task carries the identifier, or the
+    /// task has already been completed. Returning the task, not a bare success, matches
+    /// <see cref="RescheduleAsync"/>'s own shape -- a caller acts on the row just written without
+    /// a second read, the same reason <see cref="CreateAsync"/> and <see cref="RescheduleAsync"/>
+    /// already return theirs.
     /// </returns>
     /// <remarks>
     /// A second call on an already-completed task is refused with
     /// <see cref="ErrorCode.TaskAlreadyCompleted"/> rather than repeating the write: the row is
     /// left exactly as the first call set it, so <see cref="ReminderTask.CompletedAt"/> always
     /// carries the instant of the first completion, never a later one.
     /// </remarks>
-    Task<Result> CompleteAsync(Guid id, CancellationToken ct);
+    Task<Result<ReminderTask>> CompleteAsync(Guid id, CancellationToken ct);
```

In `src/Assistant.Impl/Services/TaskService.cs`, replace the `CompleteAsync` method in full:

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
    public Task<Result<ReminderTask>> ExecuteAsync(Guid taskId, string argument, CancellationToken ct) =>
        taskService.CompleteAsync(taskId, ct);
}
```

In `src/Assistant.Impl/Telegram/CallbackRouter.cs`, change the one call to `ExecuteAsync`:

```diff
-        var result = await action.ExecuteAsync(taskId, ct);
+        var result = await action.ExecuteAsync(taskId, string.Empty, ct);
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **74 passed**, 0 failed — the same count as
the baseline, with the one strengthened fact now passing (Decision 2's new assertions on
`result.Value` hold).

- [ ] **Step 3: Confirm `CallbackRouterTests.cs` is observably unaffected**

```bash
dotnet test tests/Assistant.IntegrationTests --no-build --filter "FullyQualifiedName~CallbackRouterTests"
```

Expected: **8 passed**, 0 failed — every existing `CallbackRouterTests` test, unmodified, still
green (Decision 4).

- [ ] **Step 4: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: **59 passed** unit (0 failed). **74 passed** integration (0 failed) — matching
"Validation," above, exactly.

- [ ] **Step 5: Commit**

```bash
git add tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs \
        src/Assistant.Interfaces/ITaskAction.cs \
        src/Assistant.Interfaces/ITaskService.cs \
        src/Assistant.Impl/Services/TaskService.cs \
        src/Assistant.Impl/Services/Actions/DoneAction.cs \
        src/Assistant.Impl/Telegram/CallbackRouter.cs
git commit
```

Message:

```
feat: F11-2 -- the action learns to carry an argument

ITaskAction.ExecuteAsync gains an argument parameter and returns the
task it acted on, matching the shape RescheduleAsync already has.
This is a modification of an existing interface, not a second one
invented for actions that carry an argument: DoneAction, the only
implementation today, accepts and ignores it, and CallbackRouter's
one call site is updated only as far as compiling the new signature
requires.

ITaskService.CompleteAsync widens to Task<Result<ReminderTask>> for
the same reason RescheduleAsync already returns its own row: a caller
acts on the task just completed without a second read. DoneAction's
direct forwarding of CompleteAsync's return value means both
interfaces had to widen in the same step -- there is no smaller
honest split that avoids a throwaway shim.

CallbackRouter passes string.Empty rather than switching to the
four-argument TryDecode overload: no registered action needs a real
argument yet, and decoding one now with nowhere for it to go would be
untested plumbing nothing could observe.

No button changes, no message text changes, and no documentation is
touched -- nothing here is visible from a real chat. The shared
ToMessageText renderer, projected for this slice by F11-1's own plan,
is deferred to F11-3 instead: an extraction with exactly one caller
proves nothing a unit test could add over what the integration suite
already covers, and its whole value depends on a second caller this
slice does not yet have.

Tests: 59 unit, 74 integration -- unchanged from the F11-1 baseline.
One existing fact strengthened and renamed to prove CompleteAsync now
returns the completed task, not merely that it succeeds. Build clean,
zero warnings.

Plan: docs/plans/2026-09-07-f11-2-the-action-carries-an-argument.md

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `ITaskAction.ExecuteAsync`'s signature is `Task<Result<ReminderTask>> ExecuteAsync(Guid
      taskId, string argument, CancellationToken ct)`; no second interface is added (Decision 1)
- [ ] `ITaskAction.cs`'s `<remarks>` no longer claims "snooze, reschedule and edit actions follow
      at F11, each adding one more implementation rather than changing this one" — it names
      `ScheduleAction` and states this interface was modified in place (Decision 1)
- [ ] `ITaskService.CompleteAsync` and `TaskService.CompleteAsync` both return
      `Task<Result<ReminderTask>>`, refusing with the existing `ErrorCode.TaskNotFound`/
      `TaskAlreadyCompleted` — no `ErrorCode` member is added (Decision 2)
- [ ] `DoneAction.ExecuteAsync` accepts `argument` and ignores it, forwarding directly to
      `taskService.CompleteAsync` (Decision 3); no `DoneActionTests.cs` is added (Decision 3)
- [ ] `CallbackRouter.cs:85`'s call to the three-argument `TryDecode` overload is unchanged;
      line 99 passes `string.Empty` as `ExecuteAsync`'s new argument (Decision 4)
- [ ] Every existing `CallbackRouterTests.cs` test passes unmodified — confirmed by an isolated
      re-run (8 passed) against the fully implemented draft, not merely asserted
- [ ] `MessageHandler.cs` has zero lines changed; no `ToMessageText` renderer is extracted
      (Decision 5)
- [ ] `CallbackCodec.cs`, `TaskActions.cs`, `ErrorCode.cs`, `INotifier.cs`,
      `TelegramNotifier.cs`, `CallbackCodecTests.cs`, `TelegramListenerTests.cs`,
      `DueReminderJobTests.cs`, `ImplServiceCollectionExtensions.cs`, and every documentation
      file are unchanged ("What this slice does NOT include")
- [ ] Exactly one existing `[Fact]` renamed and strengthened in `TaskServiceTests.cs`
      (`CompleteAsync_TaskWasPending_SetsStatusAndStampsCompletedAt` to `...AndReturnsIt`); no
      `[Fact]` added anywhere in this slice
- [ ] Every changed public member carries a three-line-tag `<summary>` plus every `<param>`/
      `<returns>` `CS1591`/`CS1573` requires; the strengthened test's summary is Gherkin, one
      clause per line
- [ ] No emoji anywhere, including the commit message
- [ ] **No plan-internal decision citation inside any C# code block, doc comment, or commit
      message** — every fenced code block above was re-read for this before the plan was
      finalised
- [ ] Plain ASCII `--` in every C# comment and the commit message body; this document's own
      markdown prose uses real em dashes
- [ ] No `docker compose ... --build` or migration is claimed necessary (both confirmed in
      "Verified facts")
- [ ] Build reaches `0 Warning(s)` despite `DoneAction`'s new, unused `argument` parameter —
      confirmed empirically, not assumed (no `.editorconfig`, no analyzer severity override)

**Whole feature (F11), once F11-3 and F11-4 also land:**
- [ ] This slice's diff measures 44 lines; the running total across F11's four PRs is not itself
      a budget — each is independently under the 1000-line budget regardless of the eventual sum.
- [ ] `ITaskAction.ExecuteAsync`'s widened signature and `ITaskService.CompleteAsync`'s widened
      return, both proven correct here by the existing and strengthened test suite, are consumed
      unchanged by F11-3's `ScheduleAction` and `CallbackRouter` rewrite — neither needs a second
      look at its own correctness once wired up, only at the wiring itself.
- [ ] The `ToMessageText` renderer, deferred here, is extracted at F11-3 at the exact commit that
      gives it a second caller — the point this slice's Decision 5 argued is the earliest point
      the extraction can be proven faithful rather than merely plausible.
