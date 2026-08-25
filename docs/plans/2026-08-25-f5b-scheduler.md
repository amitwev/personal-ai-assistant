# F5b — The scheduler fires due reminders

**Spec:** `docs/design/slice-1-reminders.md` §6.1, §6.2
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md`, F5b
**Depends on:** F5a (`ITaskService`, `TaskService`, `Result`), F4a/F4b (`INotifier`), F3 (`GetDueRemindersAsync`)

This is the milestone feature. When it lands, the product works end to end: seed a
row with a due time in the past, start the worker, and the phone buzzes. Every
feature before this one was a part; this is the first one a person can watch.

## Global Constraints

- **YAGNI.** Build only what F5b's stated behaviours need. A method arrives with
  its caller, never before it.
- **Open/closed.** New behaviour arrives as a new type implementing an existing
  interface. `ScheduledJobBase` exists so job number two changes no existing file.
- **`TaskService` is the single writer.** No job, ever, touches `ITaskRepository`.
  `DependencyRuleTests.Only_TaskService_references_ITaskRepository_in_Impl`
  already enforces this across every class in `Assistant.Impl` — F5b is simply the
  first feature that gives it a type to catch.
- **Test business use cases, not implementation.** Assert that a message arrived,
  not that a field changed.
- **Primary constructors** on every class that takes arguments. Never a separate
  constructor declaration.
- **`<summary>` XML tags span three lines** — open tag, text, close tag.
- **Gherkin summaries**, one clause per line, `When` / `And` / `Then`.
- **Plain xUnit `Assert`.** No Shouldly, no FluentAssertions.
- **Every enum's first member is `Unknown`**, with no explicit numeric values.
- Warnings are errors. `dotnet clean` before any build you intend to trust.

## Decisions this plan makes — review these first

### A. `TimeProvider` replaces `IClock`, which F5a shipped three days ago

F5b needs a fake **timer**, not just a fake clock: two of the loop's promises in
§6.1 ("a throwing job must never terminate the loop", "a slow job cannot overlap
itself") cannot be observed without advancing time across at least two ticks. With
a real 30-second `PeriodicTimer` those tests take a minute and are timing-flaky;
with a shortened interval they are merely less flaky.

`TimeProvider` is the BCL abstraction that supplies both halves — `GetUtcNow()`
replaces `IClock.UtcNow`, and `new PeriodicTimer(period, timeProvider)` makes the
loop advance on command. `FakeTimeProvider`
(`Microsoft.Extensions.TimeProvider.Testing`, 10.9.0) drives both from one object,
so the tests contain no sleeping and no wall-clock dependency at all.

Keeping `IClock` as well would leave two time abstractions in a codebase that has
one consumer of the first.

**This is a modification to shipped code, which cuts against the open/closed
preference this project works by.** It is worth making anyway, and worth making
now: `IClock` has exactly one consumer (`TaskService`), the replacement is the
BCL's own equivalent rather than a competing invention, and the second requirement
that proves `IClock` underpowered has arrived one feature after it shipped. The
same swap after F7, F9 and F10 have taken dependencies on it is a genuinely
expensive change. Cost if this call is wrong: reverting touches one class and one
registration.

`IClock` and `SystemClock` are deleted, not deprecated. Nothing outside
`TaskService` references them.

### B. `ITaskService` gains `GetDueRemindersAsync`, and the job never names a time

The job cannot call `ITaskRepository` — the architecture test forbids it, and §4.2
puts every rule in `TaskService`. So the read moves onto the service:

```csharp
Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(int limit, CancellationToken ct);
```

Note what is **not** in that signature: `asOfUtc`. `TaskService` holds the
`TimeProvider` and decides what "now" means. A job that passed its own timestamp
would be a second place that defines due-ness, and the two would eventually
disagree. This is the F5a pattern continuing — §4.2 lists eight methods on
`ITaskService`, and each one arrives with the caller that needs it.

### C. Three business tests through the job, two loop tests through the scheduler

The three behaviours the backlog names for F5b are all observable at the job:

| Behaviour | How it is arranged |
| :--- | :--- |
| A due task produces exactly one message | one due task, run the job once |
| A second tick produces none | same task, run the job twice |
| A restart after the due time still delivers | task due three days ago, run the job once |

The third is the one that documents §6.2's "deliberately no lower bound on
`due_at`". It looks redundant against F3's query tests, and is not: F3 proves the
SQL returns the row, this proves the *product* still delivers it. A future
"ignore anything older than an hour" optimisation would pass every F3 test and
fail this one.

Two further promises belong to the loop rather than the job, and §6.1 states them
as requirements rather than as implementation notes:

| Behaviour | Why it is not a job test |
| :--- | :--- |
| A throwing job does not terminate the loop | needs two ticks and a job that throws on the first |
| A slow job does not overlap itself | needs a tick to arrive while the previous run is still in flight |

Both are driven by `FakeTimeProvider.Advance`, so neither sleeps.

### D. Send, then mark — and the failure mode this chooses

Per §6.2. `MarkReminderSentAsync` runs only after `SendAsync` returns. If the send
throws, nothing is marked and the next tick tries again. If the process dies
between the send and the mark, the reminder is delivered twice.

That is the deliberate trade: at-least-once. A duplicate reminder is a small
annoyance; a silently dropped one destroys the product's only promise.

### E. The message is the task's title, with nothing added

No prefix, no emoji, no rendering. `notifier.SendAsync(task.Title, ct)`.

**Carried debt, and the feature that closes it:** `TelegramNotifier` sends with
`ParseMode.Html`, so a title containing `<` or `&` will be rejected by Telegram
with a 400. At F5b this is unreachable — the only way a task exists is a
hand-written SQL insert, so there is no untrusted title in the system. **F7 is the
first feature where a person can type one, and F7 owes the escaping.** Recorded
here so it is not discovered in production.

### F. Constants, not settings, for the interval and the batch size

30 seconds (§6.1) and the batch limit are `private const` on the types that use
them. Nothing needs to change either without a rebuild, and the project's
`IValidatableConfig` pattern is for values that differ between environments.
Settings arrive when something actually differs.

### G. The two xUnit collections merge into one

`PostgresCollection` and `WireMockCollection` become a single `IntegrationCollection`
carrying both fixtures. Two reasons, and the second is the sharp one:

1. F5b's job test needs a database **and** a stub, and an xUnit test class can
   belong to only one collection.
2. There is no `xunit.runner.json` and no `[assembly: CollectionBehavior]`, so
   distinct collections run **in parallel**, and `PostgresFixture.ResetAsync`
   truncates every table. Two Postgres-touching collections would truncate each
   other's rows mid-test. That failure presents as a flaky database, not as a
   test-isolation bug, which is what makes it worth pre-empting rather than
   debugging later.

## What F5b does NOT include

Spec §6 describes more than this feature builds. Each of these is deferred
deliberately, not overlooked:

- **The 24-hour collapse into a summary message** (§6.2). Needs a message-format
  decision, and the backlog names no F5b test for it. Its own feature.
- **`delivery_attempts` capped at 3** (§6.2). The column does not exist — F1 cut
  it. Reintroducing a column plus retry accounting is its own feature, and nothing
  retries yet.
- **The heartbeat file each tick touches** (§6.1, §8). Belongs with the container
  healthcheck that reads it. A file written for no reader is not a feature.
- **`DailyBriefJob`** (§6.3) and **inline buttons** (§6.4).
- **Polly retries and 429 handling** (§6.5).

## Still open after F5b

- **Lost update.** `DbSet.Update` writes every column, so a stale read silently
  reverts a concurrent change. The re-entrancy guard means one tick at a time and
  this is a single-user product, so there is no second writer yet. **F7 adds
  one** — inbound messages arrive on a different path from the scheduler. Fix
  when it does: an `xmin` concurrency token, or a targeted `UPDATE`.
- **`default(Result)` is a success.** Every enum in this project reserves its
  first member for "nobody set this"; `Result`'s default inverts that rule. Not
  reachable today because nothing constructs one by default.

## File Structure

```
src/Assistant.Interfaces/
    IClock.cs                          DELETED
    IScheduledJob.cs                   new
    ITaskService.cs                    + GetDueRemindersAsync

src/Assistant.Impl/
    Time/SystemClock.cs                DELETED
    Scheduling/ScheduledJobBase.cs     new
    Scheduling/ReminderScheduler.cs    new
    Jobs/DueReminderJob.cs             new
    Services/TaskService.cs            IClock -> TimeProvider, + GetDueRemindersAsync
    ImplServiceCollectionExtensions.cs + AddAssistantScheduler

src/Assistant.Worker/Program.cs        wire repository, services, scheduler

tests/Assistant.IntegrationTests/
    Infrastructure/PostgresCollection.cs   -> IntegrationCollection.cs
    Infrastructure/WireMockCollection.cs   DELETED (merged)
    Jobs/DueReminderJobTests.cs            new
    Scheduling/ReminderSchedulerTests.cs   new
```

## Task 1: `TimeProvider` replaces `IClock`

Delete `src/Assistant.Interfaces/IClock.cs` and `src/Assistant.Impl/Time/SystemClock.cs`
(and the now-empty `Time/` folder).

`TaskService` takes `TimeProvider` instead of `IClock`:

```csharp
internal sealed class TaskService(ITaskRepository repository, TimeProvider timeProvider) : ITaskService
```

and `clock.UtcNow` becomes `timeProvider.GetUtcNow()`. **Keep the single-read
discipline F5a established** — `MarkReminderSentAsync` reads the instant once into
a local and stamps both `ReminderSentAt` and `UpdatedAt` from it. That pairing is
the whole reason the abstraction exists; reading twice lets the pair drift.

In `AddAssistantServices`, `services.AddSingleton<IClock, SystemClock>()` becomes
`services.AddSingleton(TimeProvider.System)`.

**The `Microsoft.Extensions.TimeProvider.Testing` package is NOT added here.** No
test needs `FakeTimeProvider` until Task 4, and a package reference nothing
consumes is exactly the speculative dependency this project refuses. Task 4 adds
it.

**The binding spec changes in this same commit**, per `AGENTS.md`. Four places in
`docs/design/slice-1-reminders.md` name the abstraction being deleted:

| Line | Says | Becomes |
| :--- | :--- | :--- |
| §4 project table | `IClock` among the `Interfaces` types | drop it — `TimeProvider` is a BCL type, not one of ours |
| §4 folder tree | `SystemClock` under `Scheduling/` | drop it |
| §3.6 seam table | `IClock` / `SystemClock`, `FakeClock` | `TimeProvider` / `TimeProvider.System`, `FakeTimeProvider` |
| §7.2 | "`FakeClock` replaces `SystemClock`" | "`FakeTimeProvider` replaces `TimeProvider.System`" |

Do **not** rewrite the dated documents under `docs/plans/` or
`docs/2026-08-16-*`. Those are point-in-time records of shipped work; editing them
rewrites history rather than correcting a live claim. This is the same call Task 2
made about `PostgresCollection`.

**Verification:** the existing 19 integration tests and 18 unit tests must still
pass, unchanged. If any test needed editing beyond a type name, the swap changed
behaviour and something is wrong.

## Task 2: Merge the two collections

Replace `PostgresCollection` and `WireMockCollection` with one
`Infrastructure/IntegrationCollection.cs`:

```csharp
[CollectionDefinition(Name)]
public sealed class IntegrationCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<WireMockFixture>
{
    public const string Name = "integration";
}
```

Point all five existing test classes at `[Collection(IntegrationCollection.Name)]`.
`TelegramNotifierTests` keeps taking only `WireMockFixture`; a class need not
consume every fixture its collection offers.

**Verification:** 19 integration tests still pass. Run the suite three times — a
collection-parallelism bug is intermittent by nature, so one green run proves less
than it appears to.

## Task 3: `ITaskService.GetDueRemindersAsync`

Add to `ITaskService` (XML docs: what a caller gets, and that the service decides
what "now" means):

```csharp
Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(int limit, CancellationToken ct);
```

`TaskService` implements it as a pass-through that supplies the instant:

```csharp
public Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(int limit, CancellationToken ct) =>
    repository.GetDueRemindersAsync(timeProvider.GetUtcNow(), limit, ct);
```

**No test of its own.** It is a pass-through with no branch, and F3's eight query
tests already document which tasks come back. Task 5's job tests exercise it end
to end. A test here would assert that a method calls another method.

## Task 4: `IScheduledJob`, `ScheduledJobBase`, `ReminderScheduler`

`IScheduledJob` in `Assistant.Interfaces`:

```csharp
public interface IScheduledJob
{
    Task RunAsync(CancellationToken ct);
}
```

`ScheduledJobBase` in `Assistant.Impl/Scheduling/` — abstract, holding the two
guarantees §6.1 assigns it:

- **Re-entrancy guard.** A tick arriving while the previous run is still in flight
  is skipped, not queued. Use `Interlocked.CompareExchange` on an `int` flag —
  a `SemaphoreSlim` would make the second tick wait, which is queueing.
- **try/catch.** The job's exception is caught and swallowed at this boundary so
  it cannot reach the loop. Derived classes implement `ExecuteAsync`; `RunAsync`
  is the sealed wrapper that applies both.

`ReminderScheduler` in `Assistant.Impl/Scheduling/` — a `BackgroundService` taking
`IEnumerable<IScheduledJob>` and `TimeProvider`, with
`private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);` and a
`PeriodicTimer` built from the injected `TimeProvider`. Each tick runs every
registered job.

Add to `Directory.Packages.props`:
`<PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.9.0" />`
and reference it from `tests/Assistant.IntegrationTests` only. This is the task
that first needs `FakeTimeProvider`, which is why the package arrives here rather
than in Task 1.

`AddAssistantScheduler(this IServiceCollection services)` registers
`DueReminderJob` as an `IScheduledJob` and `ReminderScheduler` as a hosted service.
It is a separate extension from `AddAssistantServices` so a test can compose the
domain services without starting a background loop.

**Two tests**, in `tests/Assistant.IntegrationTests/Scheduling/ReminderSchedulerTests.cs`,
both driven by `FakeTimeProvider.Advance` — no sleeping, no real timer:

```
/// When a job throws on one tick
/// And the next tick arrives
/// Then the job runs again.
```
Arrange a stub `IScheduledJob` that throws the first time and records the second.
This is the test that proves the host survives a failing job.

```
/// When a job is still running as the next tick arrives
/// Then that tick is skipped rather than starting a second run.
```
Arrange a stub job that blocks on a `TaskCompletionSource`, advance twice, assert
it started exactly once, then release it.

Both stubs live in the test file. They are test doubles for `IScheduledJob`, not
for `DueReminderJob` — the loop's contract is with the interface.

## Task 5: `DueReminderJob` and the three business tests

`DueReminderJob` in `Assistant.Impl/Jobs/`, deriving from `ScheduledJobBase`,
taking `ITaskService` and `INotifier`. `private const int BatchSize = 50;`

```
tasks = await taskService.GetDueRemindersAsync(BatchSize, ct)
foreach task:
    await notifier.SendAsync(task.Title, ct)          // send
    await taskService.MarkReminderSentAsync(task.Id, ct)   // then mark
```

Send **then** mark, per decision D. Do not wrap the pair in a try/catch here —
`ScheduledJobBase` owns that boundary, and catching per task would silently swallow
a Telegram outage into an infinite quiet loop.

**Three tests**, in `tests/Assistant.IntegrationTests/Jobs/DueReminderJobTests.cs`,
on the merged collection so both fixtures are available. Build the SUT from a
provider that has the repository, the services and the notifier pointed at the
WireMock stub. Use `ReminderTaskBuilder.BuildReminderTask` — do not add a fourth
local builder.

```
/// When a task is due
/// And the job runs
/// Then exactly one message is sent, carrying the task's title.
```

```
/// When a task's reminder has already been delivered
/// And the job runs again
/// Then no second message is sent.
```
Run the job twice; assert the stub received exactly one request in total. This is
the test that proves the mark is what stops redelivery.

```
/// When a task has been due for three days
/// And the job runs
/// Then its reminder is still delivered.
```
The restart-catch-up guarantee: no lower bound on `due_at`.

## Task 6: Wire the Worker, and verify by hand

`Program.cs` currently registers only Telegram — no repository, no services, no
scheduler. Add:

```csharp
builder.Services.AddAssistantRepository(builder.Configuration.GetConnectionString("Assistant"));
builder.Services.AddAssistantServices();
builder.Services.AddAssistantScheduler();
```

Read the connection string the way the rest of the project reads settings; check
how `compose.yaml` supplies it before inventing a key name. Keep the existing
`send-test-message` branch working.

Migrations already apply on startup (`AGENTS.md`, "Run locally"). Confirm that is
still true after the wiring changes rather than assuming it.

**Then verify the milestone by hand and report the result:** `docker compose up -d --build`,
seed one row with `due_at` in the past, and confirm the phone receives it within
30 seconds. This feature's whole point is that it is observable; a green test suite
is not the same evidence.

## Task 7: Record what F5b settled

Update the F5b entry in `docs/design/2026-08-22-slice-1-feature-backlog.md` to
`**done**`, with a *Settled at F5b* list covering: the `TimeProvider` swap and why
it was worth modifying F5a's code; `GetDueRemindersAsync` landing on `ITaskService`
without an `asOfUtc` parameter; send-then-mark and the duplicate it accepts; the
collection merge and the flaky-database failure it pre-empts; and the HTML-escaping
debt that F7 owes.

If the spec's §6.1/§6.2 wording no longer matches what was built — the 24-hour
collapse and `delivery_attempts` are described there as though they exist —
`AGENTS.md` requires the spec be updated in the same commit. Mark them as deferred
rather than deleting them.

## Self-review

Before opening the PR:

- [ ] `dotnet clean && dotnet build` — zero warnings
- [ ] Integration tests green, run three times (see Task 2)
- [ ] Unit tests green
- [ ] No `IClock` or `SystemClock` reference survives in `src/`, `tests/`, or the
      binding spec — dated plan docs keep theirs
- [ ] `DueReminderJob` does not name `ITaskRepository` — and the architecture test
      is what proves it, so confirm that test actually ran
- [ ] No test sleeps, and no test depends on wall-clock time
- [ ] Diff under 1000 lines excluding this plan
- [ ] The manual milestone check in Task 6 was actually performed
