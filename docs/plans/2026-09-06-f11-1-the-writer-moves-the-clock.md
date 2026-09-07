# F11-1 — the writer moves the clock

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** F11's full feature (spec §6.4) gives every task message a `[Done] [Schedule]` button
pair, where `Schedule` opens a preset menu (`+1h`, `+3h`, `Tonight 20:00`, `Tomorrow 09:00`,
`Back`). That is too large a slice to read or review in one pull request — an earlier attempt at
planning it in one piece ran to 2401 lines and could not be read in the GitHub mobile app. F11 is
now four pull requests. This plan covers only the first: the two pieces of write-side plumbing
every later F11 slice needs and nothing calls yet. `ITaskService` gains `RescheduleAsync`, which
sets a task's due time, clears its reminder-sent marker so it fires again, and refuses on a
completed task exactly as `CompleteAsync` already does. `CallbackCodec` gains the ability to
encode and decode a callback string's optional fourth segment — the argument a button like `+1h`
will need to carry once a button exists to carry it. Nothing in this slice adds a button, changes
what any message says, or changes how a tap is routed. Both new surfaces are exercised only by
their own tests, the same posture F10-1 ("the writer") already established for
`ITaskService.CreateAsync` before anything but a test called it.

**Tech Stack:** `net10.0`, nullable enabled, warnings are errors — the existing stack. This slice
adds **no new NuGet package** and **no database migration**: `ReminderTask.DueAt` and
`ReminderTask.ReminderSentAt` are both already nullable columns, present since the very first
migration (see "Verified facts").

**Spec:** `docs/design/slice-1-reminders.md` §4.2 (`TaskService` as single writer — "the interface
grows a method per feature that needs one"), §6.4 (inline buttons and the callback wire format —
read, not corrected here; the corrections a rendered `[Done] [Schedule]` pair would justify belong
to the slice that actually ships one), §7.2 (unit vs. integration split), §7.3 (assertion
standard), §12.1 (XML docs), §12.5 (primary constructors), §12.6 (no emoji).

**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — the F11 entry (currently line
633) is read for context but not edited: it describes behaviour (a rendered second button, a
closed loop on issue #27) that only becomes true once a later F11 slice ships it. Editing it now,
ahead of any of that being true, would make the backlog say something false.

---

## How this slice fits

F11 splits into four independently reviewable pull requests, in a strict dependency order forced
by what compiles against what:

1. **F11-1 (this plan) — the writer moves the clock.** `ITaskService.RescheduleAsync` and
   `CallbackCodec`'s fourth segment, plus their tests. Nothing outside those tests calls either.
2. **F11-2 — the action learns to carry an argument.** `ITaskAction.ExecuteAsync` gains the
   `argument` parameter; `DoneAction` follows; `CompleteAsync`'s return type widens to match
   `RescheduleAsync`'s shape; the shared `ToMessageText` renderer is extracted from
   `MessageHandler`. Still no button.
3. **F11-3 — the button appears.** `TaskActions.Schedule` and `ScheduleAction` (calling
   `RescheduleAsync`); `INotifier.UpdateTaskAsync` and `TelegramNotifier`'s two-button keyboard;
   `CallbackRouter`'s switch to status-based rendering; the DI registration; the tests that
   hard-code one button; every documentation correction — all of it describes behaviour that only
   becomes true here (full list in "What this slice does NOT include").
4. **F11-4 — the menu.** `Schedule` becomes a menu opener; `+3h`, `Tonight 20:00`,
   `Tomorrow 09:00`, and `Back` join `+1h` as presets.

**Why F11-1 is buildable on its own even though nothing calls it yet.** F11's old, single-commit
plan needed `ITaskAction`, `ITaskService`, `INotifier`, and `CallbackRouter` to all agree on a new
shape at once, because it also rewrote `CallbackRouter`'s rendering logic. That interconnection is
real, but it starts at F11-2, not here: `RescheduleAsync` is a new method beside three existing
ones on an interface that already "grows a method per feature that needs one," and
`CallbackCodec`'s new capability is reached through new overloads, not a changed signature, so
`CallbackRouter.cs:85`'s existing call keeps compiling untouched. See Decision 4 for the direct
precedent this follows.

**The measured total, drafted in full, built, and tested green — not estimated.** Every line below
was produced by actually writing this slice's five files in an isolated `git worktree` branched
from this repository's own `HEAD` (`cace920`, which shares its entire `src/`/`tests/` tree with
`origin/main` at `b75c76b` — only a plan document differs), then reading `git diff --numstat`
against it directly:

| File | + | - |
| :--- | ---: | ---: |
| `src/Assistant.Interfaces/ITaskService.cs` | 29 | 0 |
| `src/Assistant.Impl/Services/TaskService.cs` | 24 | 0 |
| `src/Assistant.Impl/Telegram/CallbackCodec.cs` | 53 | 13 |
| **Code subtotal** | **106** | **13** |
| `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs` | 85 | 0 |
| `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs` | 48 | 9 |
| **Test subtotal** | **133** | **9** |
| **Grand total** | **239** | **22** |

**Total changed lines: 261** (239 insertions + 22 deletions), 739 lines under the 1000-line budget.
No documentation file is touched — see "What this slice does NOT include."

---

## Global Constraints

Every constraint the project's prior plans carry forward still applies here:

- `net10.0`; nullable enabled; warnings are errors; `CS1591`/`CS1573` are errors everywhere.
- **Every class taking arguments uses a primary constructor.** Not exercised by this slice's own
  new code (`CallbackCodec` is static; `TaskService`'s existing constructor is unchanged), but no
  separate constructor is introduced anywhere.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first. No Shouldly, no
  FluentAssertions.
- Every `<summary>` spans three lines: open tag, text, close tag. Test summaries are Gherkin
  (`When` / `And` / `Then`), one clause per line.
- No emoji anywhere: source, tests, docs, or commit messages.
- Enums: first member is `Unknown`, no explicit numeric values, new members **appended**, never
  inserted. Not exercised this slice — no enum member is added (see Decision 2).
- **Never run `docker compose down -v`.** No image rebuild or new container is needed: the
  existing `personal-ai-assistant-postgres-test-1` / `personal-ai-assistant-wiremock-1`
  containers, already running against `compose.test.yaml`'s fixed ports, are sufficient. If
  nothing is running, `docker compose -f compose.test.yaml up -d` (no `--build`) is enough.
- PR budget: 1000 changed lines per PR, excluding the plan document. This slice measures at 261
  lines — far under budget.
- Plain ASCII `--` in every C# file; real em dashes in this document's own markdown prose.
- Commit trailers, after a blank line:

  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
  ```

---

## Verified facts this plan rests on

Every fact below was checked directly against the working tree at `cace920` (`HEAD` of
`feature/f11-1-the-clock-moves`) or produced by a command actually run during this planning
session.

- **`CallbackCodec`'s only two members today are a two-argument `Encode` and a three-out-parameter
  `TryDecode`, both reading exactly three colon-separated segments.**
  `src/Assistant.Impl/Telegram/CallbackCodec.cs:27` (`Encode(string action, Guid taskId)`) and
  `:42` (`TryDecode(string data, out string action, out Guid taskId)`); the length check at `:49`
  is `parts.Length != 3`. Neither method has any notion of a fourth segment yet.
- **`CallbackCodec.TryDecode`'s only production call site is `CallbackRouter.cs:85`,**
  `CallbackCodec.TryDecode(data, out var actionKey, out var taskId)` — exactly three out
  arguments, confirmed exhaustively by `grep -rn "TryDecode" src/ tests/`. `CallbackRouter.cs` is
  not touched by this slice (see "What this slice does NOT include"), so whatever `TryDecode`
  becomes must still satisfy this exact call, unchanged, or the solution stops compiling — this is
  the fact Decision 1 is built on.
- **`ITaskService`'s existing four methods** (`src/Assistant.Interfaces/ITaskService.cs`) are
  `MarkReminderSentAsync`, `GetDueRemindersAsync`, `CompleteAsync` (returning bare
  `Task<Result>`, unchanged by this slice), and `CreateAsync` (returning
  `Task<Result<ReminderTask>>` since F10-1). `TaskService.CompleteAsync`
  (`src/Assistant.Impl/Services/TaskService.cs:49-71`) already refuses a completed task with
  `if (task.Status == ReminderStatus.Completed) { return Result.Failure(ErrorCode.TaskAlreadyCompleted); }` —
  the exact check `RescheduleAsync` reuses.
- **`ErrorCode.TaskNotFound` and `ErrorCode.TaskAlreadyCompleted` already exist**
  (`src/Assistant.Contracts/ErrorCode.cs:16` and `:52`). `RescheduleAsync` needs no new member —
  see Decision 2.
- **No migration is needed.** `src/Assistant.Models/ReminderTask.cs:32` declares `public
  DateTimeOffset? DueAt { get; set; }` and `:41` declares `public DateTimeOffset? ReminderSentAt
  { get; set; }` — both already nullable, present since
  `src/Assistant.Repository/Migrations/20260822103957_InitialCreate.cs`; neither migration since
  (`20260822202918_AddDueReminderIndex`, `20260902181436_AddCompletedAt`) touches either column.
- **`tests/Assistant.IntegrationTests/Infrastructure/ReminderTaskBuilder.cs:23-37`'s
  `BuildReminderTask` already accepts `dueAt`, `status`, `reminderSentAt`, and `completedAt`, all
  optional.** Every scenario this slice's tests need — an overdue reminder already sent, a task
  with no due time, an already-completed task — is buildable with the existing factory; no change
  to it is needed.
- **Baseline test counts, run directly against this exact `HEAD`, not assumed.**
  `dotnet build --no-restore` — `Build succeeded. 0 Warning(s). 0 Error(s).` `dotnet test
  tests/Assistant.UnitTests --no-build` — **57 passed**, 0 failed. Against the already-running
  `personal-ai-assistant-postgres-test-1` and `personal-ai-assistant-wiremock-1` containers (both
  `Up ... (healthy)` throughout this planning session), `dotnet test
  tests/Assistant.IntegrationTests --no-build` — **70 passed**, 0 failed.
- **This plan's own draft was built and tested green, not merely written.** All five files below
  were applied inside an isolated `git worktree` branched from this repository's own `HEAD`
  (never touching this working tree's `src/` or `tests/`), following the exact two-cycle
  red/green sequence given in "Steps," and reached: `Build succeeded. 0 Warning(s). 0 Error(s).`,
  **59 unit / 74 integration passed.** The four new `RescheduleAsync` tests were additionally run
  three more times in isolation (`dotnet test --filter "FullyQualifiedName~RescheduleAsync"`) with
  no failure (**4 passed**, 0 failed, all three runs). The worktree and its branch were then
  removed.

---

## Inherited context: what this slice reads from earlier features

`ITaskService`'s existing four methods (F5a, F6-1, F10-1) are read, not modified — `RescheduleAsync`
is added beside them, following the same "one method per feature that needs one" growth spec §4.2
already describes. `TaskService.CompleteAsync`'s already-completed check (F6-1) is read and reused
verbatim for `RescheduleAsync`'s own refusal. `CallbackCodec`'s `v1:` prefix and three-segment
shape (F6-2) are extended, not replaced — every button already in the owner's chat history keeps
decoding exactly as before, and `CallbackRouter`'s own call to it (F6-2, F6-3) needs no change at
all in this slice.

---

## Decisions

### 1. `CallbackCodec.TryDecode` gains a second, four-out-parameter overload — not a changed
   signature — and `Encode` gains a second, three-argument overload, not a default parameter

**Decision:**

```csharp
public static bool TryDecode(string data, out string action, out Guid taskId) =>
    TryDecode(data, out action, out taskId, out _);

public static bool TryDecode(string data, out string action, out Guid taskId, out string argument)
{
    // accepts three or four colon-separated segments; argument = "" for a three-segment string
}

public static string Encode(string action, Guid taskId, string argument) =>
    $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}:{argument}";
```

The existing two-argument `Encode` and three-out-parameter `TryDecode` are both kept, unchanged in
their own parameter lists.

**Why an overload was required here, not merely preferred — a correction, stated plainly.** An
earlier plan for this exact codec change, written for the larger F11 slice this one replaces,
changed `TryDecode`'s signature in place, because the same commit also rewrote
`CallbackRouter.cs:85`'s call to match. That is not available here: this slice does not touch
`CallbackRouter` (its rendering logic is F11-3's), so `CallbackRouter.cs:85`'s existing
three-argument call must keep compiling unchanged. An overload is therefore not a style
preference; it is the only shape that lets `CallbackCodec` grow without an edit outside this
slice's scope.

**Why the three-out-parameter overload delegates rather than duplicating the parsing logic.**
Two independent implementations — the old one still rejecting a four-segment string, the new one
accepting it — would mean the same wire string decodes differently depending on which overload is
called, which is worse than the mild, disclosed consequence of delegation: the old overload now
also accepts a four-segment string, discarding the trailing segment. This is harmless — no button
produces one until F11-3's `ScheduleAction`, and `CallbackRouter` itself moves to the four-out
overload in that same slice to read it. No existing test exercises this case
(`CallbackRouterTests.cs`, `TelegramListenerTests.cs`, `DueReminderJobTests.cs` are all untouched
and produce no such data), so the consequence is disclosed here rather than pinned down with a
test for a case nothing yet reaches.

**Why a second `Encode` overload, not a default parameter.** A default parameter would make
`Encode("done", id)` and `Encode("done", id, "")` look nearly identical while producing different
wire strings — the first omits the fourth segment entirely, and a future caller wanting a
four-segment string with a deliberately empty argument would have no way to ask for it. Two named
overloads make the two wire shapes explicit at the call site instead of implicit in a runtime
value.

### 2. `ITaskService.RescheduleAsync` returns `Task<Result<ReminderTask>>`, reusing existing
   `ErrorCode` members; no new enum member is added

**Decision:**

```csharp
Task<Result<ReminderTask>> RescheduleAsync(Guid id, DateTimeOffset dueAtUtc, CancellationToken ct);
```

Refused with `ErrorCode.TaskNotFound` when no such task exists, and with
`ErrorCode.TaskAlreadyCompleted` on a completed task — both members already exist
(`ErrorCode.cs:16`, `:52`); no enum member is appended.

**Why `Task<Result<ReminderTask>>`, not the bare `Task<Result>` `CompleteAsync` still returns.**
Each method returns what its own callers need, the argument `CreateAsync` already settled at
F10-1: it returns the row it just wrote so its caller can use the row's own fields without a
second read. `RescheduleAsync`'s eventual caller, `ScheduleAction` (F11-3), is in the identical
position — it needs the task's new `DueAt` to build the message `CallbackRouter` will show, and
re-fetching a row the single writer just wrote would be redundant. `CompleteAsync` stays bare in
this slice only because nothing here touches it; its own return type widens at F11-2, for a
reason specific to that slice. Until then `ITaskService` simply has two methods with two result
shapes, each earning its own shape from its own callers — no different from
`MarkReminderSentAsync`'s bare `Task<Result>` sitting beside `CreateAsync`'s today.

**Refusing an already-completed task, argued both ways.** *For refusing:* a completed task's own
message has already lost its keyboard, so `RescheduleAsync` is reachable on one only through a
stale button left in chat history or a crafted callback string. Reopening a task the owner
believes finished, silently, because of a tap on dead UI, is worse than a refusal. *For allowing
it (rejected):* rescheduling only moves a date and does not discard a completion record, so it
looks less destructive than a second `CompleteAsync` call. But "less destructive" is not
"harmless" — a task silently reopening from an old button is a correctness surprise regardless of
size, and `CompleteAsync`'s own existing refusal on a second completion already establishes a
completed task as terminal. Consistency with that precedent settles it.

### 3. `RescheduleAsync` accepts a task with no due time at all — this is not a guard to add,
   it is the point

**Decision:** No `DueTimeMissing`-style check on the task's current `DueAt`. A `null` `DueAt` is
rescheduled exactly like one that already has a due time: the new instant is set,
`ReminderSentAt` is cleared, `UpdatedAt` is stamped.

**Why.** `MarkReminderSentAsync` refuses a `null` `DueAt` because there is no reminder to have
been delivered — a guard against recording a delivery that never happened. `RescheduleAsync` is
not recording a delivery; it is setting a due time, and a task with none yet is exactly the case
where setting one for the first time is correct, not exceptional. Refusing here would make
`RescheduleAsync` unusable for precisely the task it would help most — one captured with no time
— and costs nothing to allow, since the method already reads and writes `DueAt` unconditionally.

### 4. This slice adds a method and a codec overload that nothing calls yet — deliberate,
   with direct precedent on this project

**The consequence, stated plainly.** After this slice merges, `RescheduleAsync` and the codec's
new overloads are reachable only from their own tests. This is exactly the situation F10-1's own
plan described for `ITaskService.CreateAsync`: "Nothing calls `CreateAsync` from outside a test
after this slice merges. Nothing is supposed to yet" — and the backlog records F10-1 as a fully
independent, fully tested pull request on that basis. The same plan: "each slice ships a fully
working, fully tested layer that nothing outside its own tests calls yet."

**Why this is the right shape, not a workaround.** Waiting until F11-3 to write this plumbing in
the same commit that first calls it would recreate exactly the interconnected, hard-to-review
commit this four-way split exists to avoid. Building it first, proven correct by its own tests,
means F11-3's diff is entirely about wiring an already-correct `RescheduleAsync` to a button, not
also debugging whether rescheduling works. No fake caller is introduced to soften this: there is
no `ScheduleAction`, no stub, no placeholder invocation anywhere in this slice.

### 5. No documentation is touched

**Decision:** Spec §6.4, spec §4.2, and the backlog's F11 entry are read for context but not
edited.

**Why.** Every sentence those sections would need corrected describes behaviour visible from a
button, a rendered message, or a real-phone verification — none of which exists yet after this
slice: no button, no renderer change, no observable behaviour at all. Editing spec or backlog
prose to describe a capability two internal methods have gained, with nothing able to invoke
either from a real interaction, would make the documentation describe something a user cannot do
— the drift `AGENTS.md`'s "correct it in the same commit" rule exists to prevent, not invite. The
correction lands at F11-3, the slice that makes the button real.

---

## What this slice does NOT include

- **`ITaskService.CompleteAsync`'s return type change**, `ITaskAction`'s signature change and
  `DoneAction`'s update, and the shared `ToMessageText` renderer extracted from `MessageHandler`.
  All **F11-2**'s — none has a caller or a second consumer yet.
- **`ErrorCode.TaskActionArgumentUnrecognized`, `TaskActions.Schedule`, `ScheduleAction` itself,
  `INotifier.UpdateTaskAsync`, `TelegramNotifier`'s two-button keyboard, `CallbackRouter`'s switch
  to status-based rendering, the DI registration, and the three existing tests
  (`DueReminderJobTests.RunAsync_TaskIsDue_AttachesTheDoneButtonForThatTask` and
  `TelegramListenerTests`'s two capture-reply tests) that assert exactly one button.** All
  **F11-3**'s. All three of those tests still pass unmodified after this slice, since nothing here
  changes how many buttons render. This slice proves `RescheduleAsync` works; F11-3 is the first
  thing that calls it from outside a test, and `CallbackRouter.cs` is not touched at all here (see
  "Verified facts").
- **Any documentation correction** — spec §6.4's button table, spec §4.2's `RescheduleAsync`
  bullet, the backlog's F11 entry, the `DeliveryAttempts` row, and `docs/e2e-local.md`. All
  **F11-3**'s — see Decision 5.
- **The `[Schedule]` button becoming a menu, and the `+3h`/`Tonight 20:00`/`Tomorrow 09:00`/`Back`
  presets.** **F11-4**'s.
- **Any `ILocalTimeResolver` use.** `RescheduleAsync` takes an already-resolved `dueAtUtc`, exactly
  as `CreateAsync` already does; resolving a preset into that instant is `ScheduleAction`'s job.
- **A new project reference, a new NuGet package, or any `Assistant.Repository`/migration change.**
  Confirmed by "Verified facts."
- **Closing GitHub issue #27.** Nothing in this slice is reachable from outside a test, so nothing
  changes what the owner can do about an undated task.

---

## File Structure

```
src/Assistant.Interfaces/
    ITaskService.cs                                     + RescheduleAsync

src/Assistant.Impl/
    Services/TaskService.cs                             + RescheduleAsync implementation
    Telegram/CallbackCodec.cs                            + 4-out TryDecode overload,
                                                          + 3-arg Encode overload

tests/Assistant.IntegrationTests/
    Services/TaskServiceTests.cs                         + 4 RescheduleAsync Facts

tests/Assistant.UnitTests/
    Telegram/CallbackCodecTests.cs                       + 2 Facts, 1 renamed, stale row replaced
```

No `Assistant.Models`, `Assistant.Contracts`, `Assistant.Repository`, `Assistant.Worker`, migration
file, `CallbackRouter.cs`, `TelegramNotifier.cs`, `ITaskAction.cs`, `DoneAction.cs`,
`TaskActions.cs`, `ImplServiceCollectionExtensions.cs`, or documentation file is touched.

---

## Validation

**Test count arithmetic.** Baseline, run directly against this exact `HEAD` (see "Verified
facts"): 57 unit, 70 integration.

- Unit: `CallbackCodecTests.cs` gains 2 net new `[Fact]`s (`Encode_KnownTaskIdAndArgument_...` and
  `TryDecode_WellFormedFourSegmentString_...`) — one existing `[Fact]` is renamed (no count change)
  and one `[InlineData]` row is replaced, not added (no count change). 57 + 2 = **59**.
- Integration: `TaskServiceTests.cs` gains 4 `[Fact]`s, all named `RescheduleAsync_...`. No other
  integration test file changes — see "File Structure." 70 + 4 = **74**.

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

No `docker compose ... --build` is required — no stub mapping changes. If the
`personal-ai-assistant-postgres-test-1`/`personal-ai-assistant-wiremock-1` containers are not
already running, `docker compose -f compose.test.yaml up -d` (no `--build`) is sufficient.

---

## Steps

**Decisions this slice carries:** all five, given in full above.

**Consumes:** `CallbackCodec`'s existing three-segment shape (F6-2), `ITaskService`'s "grows a
method per feature" posture (F5a, F6-1, F10-1), `TaskService.CompleteAsync`'s already-completed
check (F6-1), `ReminderTaskBuilder`'s existing optional parameters.
**Produces:** `ITaskService.RescheduleAsync`; `CallbackCodec`'s two new overloads; four new
integration tests; two new unit tests plus one renamed and one theory row replaced.

**Why one pull request, not two.** `CallbackCodec`'s change and `RescheduleAsync`'s change share
no code and touch no common file — either could stand alone. They are kept together because both
are needed, together, by the same next slice (F11-3), and neither is independently interesting to
a reviewer beyond "plumbing for a button that does not exist yet." Nothing forces one *commit* for
compilation reasons, unlike the old, larger F11 plan: each piece below gets its own real
failing-test-first cycle.

### Commit 1: the writer moves the clock

**Files:**
- Modify: `src/Assistant.Interfaces/ITaskService.cs`
- Modify: `src/Assistant.Impl/Services/TaskService.cs`
- Modify: `src/Assistant.Impl/Telegram/CallbackCodec.cs`
- Modify: `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`
- Modify: `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs`

- [ ] **Step 1: Write every `CallbackCodec` test this slice needs — watch it fail to compile**

Replace `tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs` in full — the stale four-segment
row in the malformed-strings theory (previously asserted to fail; this slice makes that shape
valid) is replaced with a five-segment one:

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

```bash
dotnet build --no-restore
```

Expected: the build **fails** with exactly these five errors (captured directly from this
planning session's own isolated worktree, at this precise state):

```
CallbackCodecTests.cs(32,34): error CS1501: No overload for method 'Encode' takes 3 arguments
CallbackCodecTests.cs(52,37): error CS1501: No overload for method 'TryDecode' takes 4 arguments
CallbackCodecTests.cs(71,34): error CS1501: No overload for method 'Encode' takes 3 arguments
CallbackCodecTests.cs(74,37): error CS1501: No overload for method 'TryDecode' takes 4 arguments
CallbackCodecTests.cs(97,37): error CS1501: No overload for method 'TryDecode' takes 4 arguments

5 Error(s)
```

Every one of these five names the exact two overloads Step 2 adds.

- [ ] **Step 2: Implement `CallbackCodec`'s fourth segment — watch it pass**

Replace `src/Assistant.Impl/Telegram/CallbackCodec.cs` in full:

```csharp
namespace Assistant.Impl.Telegram;

/// <summary>
/// Encodes and decodes the <c>callback_data</c> string carried on an inline button.
/// </summary>
/// <remarks>
/// The wire format is <c>v1:&lt;action&gt;:&lt;base64-id&gt;[:&lt;arg&gt;]</c>, per spec 6.4. The
/// four-argument <see cref="TryDecode(string,out string,out System.Guid,out string)"/> overload
/// reads the trailing <c>:&lt;arg&gt;</c> segment -- nothing consumes it yet (F11-2's and F11-3's
/// job). The original three-out-parameter overload keeps its exact parameter list, so
/// <c>CallbackRouter</c>'s existing call needs no change, and delegates to the four-argument one
/// so a given wire string decodes the same way regardless of which overload is called.
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
    /// <param name="argument">The argument <see cref="TryDecode(string,out string,out Guid,out string)"/> reads back.</param>
    /// <returns>
    /// A string of the form <c>v1:&lt;action&gt;:&lt;base64-id&gt;:&lt;arg&gt;</c> -- 37 characters
    /// for the key <c>schedule</c> and the argument <c>+1h</c>, comfortably inside Telegram's
    /// 64-byte callback data limit.
    /// </returns>
    public static string Encode(string action, Guid taskId, string argument) =>
        $"{Prefix}:{action}:{Convert.ToBase64String(taskId.ToByteArray())}:{argument}";

    /// <summary>
    /// Attempts to decode a callback data string, without reading its optional argument.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <returns>
    /// <see langword="true"/> for a well-formed three- or four-segment string; delegates to the
    /// four-argument overload, discarding its argument.
    /// </returns>
    public static bool TryDecode(string data, out string action, out Guid taskId) =>
        TryDecode(data, out action, out taskId, out _);

    /// <summary>
    /// Attempts to decode a callback data string, including its optional argument.
    /// </summary>
    /// <param name="data">The raw string from <c>CallbackQuery.Data</c>.</param>
    /// <param name="action">The decoded action key, or empty when decoding fails.</param>
    /// <param name="taskId">The decoded task identifier, or <see cref="Guid.Empty"/> when decoding fails.</param>
    /// <param name="argument">
    /// The decoded fourth segment, or an empty string when <paramref name="data"/> carried only
    /// three segments or decoding failed altogether.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for a well-formed three- or four-segment string with the right
    /// version prefix and a 16-byte base64 id segment; <see langword="false"/> otherwise.
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

```bash
dotnet build --no-restore
dotnet test tests/Assistant.UnitTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **59 passed**, 0 failed — the 57-test
baseline plus this step's two net-new facts.

- [ ] **Step 3: Write every `RescheduleAsync` test this slice needs — watch it fail to compile**

In `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`, insert before the
`CreateAsync_TitleAndResolvedDueTime_StoresAPendingTaskAndReturnsIt` test:

```csharp

    /// <summary>
    /// When a task whose reminder already fired is rescheduled
    /// Then its due time becomes the given instant
    /// And its reminder-sent marker is cleared so it fires again
    /// And the rescheduled task is handed back to the caller.
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
        Assert.Equal(newDueAt, result.Value!.DueAt);
        Assert.Null(result.Value.ReminderSentAt);
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
        Assert.Equal(newDueAt, result.Value!.DueAt);
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

```bash
dotnet build --no-restore
```

Expected: the build **fails** with exactly these four errors (captured directly from this
planning session's own isolated worktree):

```
TaskServiceTests.cs(172,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
TaskServiceTests.cs(196,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
TaskServiceTests.cs(219,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...
TaskServiceTests.cs(236,33): error CS1061: 'ITaskService' does not contain a definition for 'RescheduleAsync' ...

4 Error(s)
```

- [ ] **Step 4: Implement `ITaskService.RescheduleAsync` — watch it pass**

In `src/Assistant.Interfaces/ITaskService.cs`, append after `CreateAsync`'s closing `;`:

```diff
     Task<Result<ReminderTask>> CreateAsync(
         CreateTaskRequest request, DateTimeOffset? dueAtUtc, CancellationToken ct);
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
+    /// The rescheduled task, or the reason it was refused: no task carries the identifier, or the
+    /// task has already been completed. Returning the task, not a bare success, lets a caller act
+    /// on the new due time without a second read -- <see cref="CreateAsync"/> does the same.
+    /// </returns>
+    /// <remarks>
+    /// Snooze and reschedule are the same operation: both set <see cref="ReminderTask.DueAt"/> and
+    /// clear <see cref="ReminderTask.ReminderSentAt"/> to <see langword="null"/>. A task with no
+    /// due time at all may also be rescheduled -- this gives it one for the first time. A
+    /// completed task is refused with <see cref="ErrorCode.TaskAlreadyCompleted"/>, the same rule
+    /// <see cref="CompleteAsync"/> uses for a second completion.
+    /// </remarks>
+    Task<Result<ReminderTask>> RescheduleAsync(Guid id, DateTimeOffset dueAtUtc, CancellationToken ct);
 }
```

In `src/Assistant.Impl/Services/TaskService.cs`, append after `CreateAsync`'s closing brace:

```diff
         return Result<ReminderTask>.Success(task);
     }
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
+    }
 }
```

```bash
dotnet build --no-restore
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` **74 passed**, 0 failed — the 70-test
baseline plus this step's four new facts.

- [ ] **Step 5: Run the whole suite**

```bash
dotnet test tests/Assistant.UnitTests --no-build
dotnet test tests/Assistant.IntegrationTests --no-build
```

Expected: **59 passed** unit (0 failed). **74 passed** integration (0 failed) — matching
"Validation," above, exactly.

- [ ] **Step 6: Commit**

```bash
git add src/Assistant.Interfaces/ITaskService.cs \
        src/Assistant.Impl/Services/TaskService.cs \
        src/Assistant.Impl/Telegram/CallbackCodec.cs \
        tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs \
        tests/Assistant.UnitTests/Telegram/CallbackCodecTests.cs
git commit
```

Message:

```
feat: F11-1 -- the writer moves the clock

ITaskService gains RescheduleAsync: sets a task's due time, clears
its reminder-sent marker so it fires again, and refuses on a
completed task exactly as CompleteAsync already does. It returns the
rescheduled task, not a bare success, so a future caller can act on
the new due time without a second read -- the same reason CreateAsync
already returns the row it just wrote.

CallbackCodec gains the ability to carry a callback button's fourth,
optional argument segment: a new four-out-parameter TryDecode overload
and a new three-argument Encode overload. The existing three-out-
parameter TryDecode keeps its exact parameter list and now delegates
to the four-argument one, so CallbackRouter's existing call keeps
compiling untouched -- this slice does not touch CallbackRouter.

Nothing outside this slice's own tests calls either new surface yet.
That is deliberate: F10-1 established the same shape for
ITaskService.CreateAsync before anything but a test called it. The
first real caller for both arrives at F11-2 and F11-3.

Tests: 59 unit, 74 integration, up from 57 / 70. Build clean, zero
warnings.

Plan: docs/plans/2026-09-06-f11-1-the-writer-moves-the-clock.md

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01Bimk3DeBJ8apMsGw5ag8qo
```

---

## Self-review

**This commit:**
- [ ] `RescheduleAsync`'s signature is `Task<Result<ReminderTask>> RescheduleAsync(Guid id,
      DateTimeOffset dueAtUtc, CancellationToken ct)`, refusing with the existing
      `ErrorCode.TaskNotFound`/`TaskAlreadyCompleted` — no `ErrorCode` member is added (Decision 2)
- [ ] `RescheduleAsync` sets `DueAt`, clears `ReminderSentAt`, and stamps `UpdatedAt`
      unconditionally on whether `DueAt` was previously set (Decision 3)
- [ ] `CallbackCodec`'s original three-out-parameter `TryDecode` keeps its exact parameter list,
      and `CallbackRouter.cs:85`'s existing call compiles with no edit to that file; the new
      four-out overload accepts both a three- and a four-segment string (Decision 1)
- [ ] `Encode` gained a second, three-argument overload — no default parameter (Decision 1)
- [ ] `ITaskAction.cs`, `DoneAction.cs`, `CallbackRouter.cs`, `TelegramNotifier.cs`, `INotifier.cs`,
      `TaskActions.cs`, `ImplServiceCollectionExtensions.cs`, and every documentation file are
      unchanged ("What this slice does NOT include")
- [ ] Exactly two new `[Fact]`s in `CallbackCodecTests.cs`, one renamed, one `[InlineData]` row
      replaced (not added); exactly four new `[Fact]`s in `TaskServiceTests.cs`, no existing test
      in that file changed
- [ ] Every new or changed public member carries a three-line-tag `<summary>` plus every
      `<param>`/`<returns>` `CS1591`/`CS1573` requires; test summaries are Gherkin, one clause per
      line, in all six new tests
- [ ] No emoji anywhere, including the commit message
- [ ] **No plan-internal decision citation inside any C# code block, doc comment, or commit
      message** — every fenced code block above was re-read for this before the plan was finalised
- [ ] Plain ASCII `--` in every C# comment and the commit message body; this document's own
      markdown prose uses real em dashes
- [ ] No `docker compose ... --build` or migration is claimed necessary (both confirmed in
      "Verified facts")

**Whole feature (F11), once F11-3 and F11-4 also land:**
- [ ] This slice's diff measures 261 lines; the running total across F11's four PRs is not itself
      a budget — each is independently under the 1000-line budget regardless of the eventual sum.
- [ ] `RescheduleAsync` and `CallbackCodec`'s fourth segment, both proven correct here by their own
      tests, are consumed unchanged by F11-3 — neither needs a second look at its own correctness
      once wired up, only at the wiring itself.
