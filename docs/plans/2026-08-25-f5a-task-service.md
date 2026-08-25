# F5a — `TaskService`, the single writer

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** `ITaskService.MarkReminderSentAsync` — the only way a reminder gets recorded as
delivered — with the rules spec §4.2 puts on it.

**Why split from F5:** F5 as one PR is `Result`, `ErrorCode`, `IClock`, `ITaskService`,
`TaskService`, `UpdateAsync`, `IScheduledJob`, `ScheduledJobBase`, `ReminderScheduler`,
`DueReminderJob`, Worker wiring, an architecture test and a collection merge. Past the 1000-line
budget, and two unrelated concerns: a domain writer and a hosting loop. F5a is the writer. F5b
makes it happen on a timer and is the observable milestone.

**Tech Stack:** EF Core 10.0.11, xUnit 2.9.3, Docker Compose.

**Spec:** `docs/design/slice-1-reminders.md` §3.6, §4.2, §7.1, §7.2.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F5.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error everywhere now.
- **Every class taking arguments uses a primary constructor.** No separate constructors.
- Every enum's first member is `Unknown`, with no explicit numeric values.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first.
- Every `<summary>` is three lines; test summaries are Gherkin, one clause per line.
- Central package management; no inline `Version=` (NU1008). **No new packages in this feature.**
- PR budget: 1000 lines. Estimated ~300.

---

## Decisions this plan makes — review these first

### A. `IClock` moves into F5a. I said F5b; that was wrong.

Spec §4.2 requires `UpdatedAt` to be stamped on every mutation. Stamping it from
`DateTimeOffset.UtcNow` inside `TaskService` would make the rule untestable and would put a
non-deterministic call inside the one class the spec says must be directly unit-testable (§4.2's
third defence).

So `IClock` and `SystemClock` land here, with the service taking `IClock`. This is a correction to
the split I proposed, not a scope grab: without it F5a cannot honour a rule the spec puts on it.

`FakeClock` is **not** built. See Decision D — no test needs it.

### B. `Result` and `ErrorCode` are designed here, because the spec never defines them

They appear nowhere in `slice-1-reminders.md`. §4.2 lists `ITaskService` methods returning
`Task<Result>` and stops there. Minimum that serves this feature:

```csharp
public readonly record struct Result(ErrorCode? Error)
{
    public bool IsSuccess => Error is null;

    public static Result Success() => new((ErrorCode?)null);

    public static Result Failure(ErrorCode error) => new(error);
}
```

- **Nullable `Error` rather than an `Unknown`-means-success sentinel.** With `Unknown` first in
  every enum by project rule, a success carrying `ErrorCode.Unknown` would read as "something went
  wrong and we don't know what". Null says "nothing went wrong".
- **A positional record struct**, because the project forbids separate constructors, so the private
  constructor a factory-only type would want is not available.
- **No message string.** Nothing renders one yet. F10 is the first feature that shows a user an
  error, and it will know better than this layer what to say.

`ErrorCode` carries only what `MarkReminderSentAsync` can produce:

```csharp
public enum ErrorCode
{
    Unknown,
    TaskNotFound,
    DueTimeMissing,
}
```

### C. `ITaskService` starts with one method, not the eight in §4.2

Spec §4.2 shows the finished interface. Backlog §1 says a feature introduces only what it tests.
`CompleteAsync`, `CancelAsync`, `SnoozeAsync`, `RescheduleAsync`, `RecordDeliveryFailureAsync`,
`QueryAsync` and `CreateAsync` arrive with the features that call them.

This is the data-access shape the backlog already ruled on: **adding a method to a service
interface is a modification, and no design avoids it.** Open/Closed applies to behaviour seams —
`IScheduledJob`, `ITaskAction` — not to this.

### D. Three tests, all integration, all asserting business outcomes

The instruction is to test business use cases, not implementation. That rules out the obvious
assertion — `ReminderSentAt == someInstant` — which pins a field write rather than a behaviour.

**The business rule is that a delivered reminder is never delivered twice.** So the headline test
asserts the task **stops being due**:

```csharp
var stillDue = await repository.GetDueRemindersAsync(AsOf, NoLimit, ct);
Assert.Empty(stillDue);
```

That is the outcome the scheduler depends on at F5b, expressed without naming a single field. It
would survive a change of column, of flag, or of mechanism.

**Why all three are integration, not unit.** Spec §7.2 reserves unit tests for rules with *no
observable side effect*. All three of these are observable through the service's own return value
or through the due query, so §7.2's "one test per behaviour, at the highest-fidelity level that
can reach it" puts them at the integration level. It also means **no fake repository is built** —
§7.2 is explicit that this project keeps no fakes to drift.

| Test | Business rule |
| :--- | :--- |
| `MarkReminderSentAsync_TaskWasDue_StopsBeingDue` | A delivered reminder is never delivered twice |
| `MarkReminderSentAsync_TaskHasNoDueTime_IsRejected` | There was no reminder to send, so none can be recorded (§4.2) |
| `MarkReminderSentAsync_TaskDoesNotExist_IsRejected` | A missing task fails loudly rather than silently doing nothing |

**Not tested, and why:** that `UpdatedAt` holds a particular instant — nothing consumes it yet, and
asserting it would pin a field rather than a behaviour. The stamping is implemented because §4.2
requires it; the first feature that reads it brings the test that pins it.

### E. `ITaskRepository` gains `UpdateAsync`, and nothing else

`MarkReminderSentAsync` reads a task, changes it, and saves it. `FindAsync` already exists.

---

## What F5a does NOT include

| Excluded | Where it goes |
| :--- | :--- |
| `IScheduledJob`, `ScheduledJobBase`, `ReminderScheduler`, `DueReminderJob` | F5b |
| Worker wiring of a hosted service, the heartbeat file | F5b, F14 |
| Merging `PostgresCollection` and `WireMockCollection` | F5b — it is the first feature needing both |
| The architecture test forbidding `Impl.Services.Jobs` from touching a repository | F5b — no job exists yet, so it would pass over zero types |
| `FakeClock` | Whenever a test needs to control time; none here does |
| Every other `ITaskService` method | F6, F10, F11, F12 |

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `src/Assistant.Contracts/Result.cs` | **Create.** Success or a reason. |
| `src/Assistant.Contracts/ErrorCode.cs` | **Create.** Two reasons, plus `Unknown`. |
| `src/Assistant.Interfaces/IClock.cs` | **Create.** One property. |
| `src/Assistant.Interfaces/ITaskService.cs` | **Create.** One method. |
| `src/Assistant.Interfaces/ITaskRepository.cs` | **Modify.** Add `UpdateAsync`. |
| `src/Assistant.Impl/Time/SystemClock.cs` | **Create.** The real clock. |
| `src/Assistant.Impl/Services/TaskService.cs` | **Create.** The single writer. |
| `src/Assistant.Impl/ImplServiceCollectionExtensions.cs` | **Modify.** Register the clock and the service. |
| `src/Assistant.Repository/Repositories/EfTaskRepository.cs` | **Modify.** Implement `UpdateAsync`. |
| `tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs` | **Create.** Three tests. |

**Interfaces produced:**
- `IClock.UtcNow` → `DateTimeOffset`
- `ITaskService.MarkReminderSentAsync(Guid id, CancellationToken ct)` → `Task<Result>`
- `ITaskRepository.UpdateAsync(ReminderTask task, CancellationToken ct)` → `Task`
- `AddAssistantServices(IServiceCollection)` → registers `IClock` and `ITaskService`

---

## Task 1: `Result`, `ErrorCode`, and `IClock`

- [ ] **Step 1: `ErrorCode`**

`src/Assistant.Contracts/ErrorCode.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>
/// Why an operation was refused.
/// </summary>
public enum ErrorCode
{
    /// <summary>
    /// Unset default. Never valid to return.
    /// </summary>
    Unknown,

    /// <summary>
    /// No task carries the requested identifier.
    /// </summary>
    TaskNotFound,

    /// <summary>
    /// The task has no due time, so there is no reminder to act on.
    /// </summary>
    DueTimeMissing,
}
```

- [ ] **Step 2: `Result`**

`src/Assistant.Contracts/Result.cs`:

```csharp
namespace Assistant.Contracts;

/// <summary>
/// The outcome of an operation that either succeeds or is refused for a stated reason.
/// </summary>
/// <param name="Error">The reason it was refused, or <see langword="null"/> when it succeeded.</param>
/// <remarks>
/// The reason is nullable rather than defaulting to <see cref="ErrorCode.Unknown"/>: every enum in
/// this project reserves its first member for "nobody set this", so a success carrying
/// <c>Unknown</c> would read as a failure whose cause was lost.
/// </remarks>
public readonly record struct Result(ErrorCode? Error)
{
    /// <summary>
    /// Whether the operation was carried out.
    /// </summary>
    public bool IsSuccess => Error is null;

    /// <summary>
    /// The outcome of an operation that was carried out.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new((ErrorCode?)null);

    /// <summary>
    /// The outcome of an operation that was refused.
    /// </summary>
    /// <param name="error">Why it was refused.</param>
    /// <returns>A failed result carrying the reason.</returns>
    public static Result Failure(ErrorCode error) => new(error);
}
```

- [ ] **Step 3: `IClock`**

`src/Assistant.Interfaces/IClock.cs`:

```csharp
namespace Assistant.Interfaces;

/// <summary>
/// The current instant, injected so time-dependent rules can be tested.
/// </summary>
public interface IClock
{
    /// <summary>
    /// The current instant in UTC.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
```

`src/Assistant.Impl/Time/SystemClock.cs`:

```csharp
using Assistant.Interfaces;

namespace Assistant.Impl.Time;

/// <summary>
/// The real clock.
/// </summary>
internal sealed class SystemClock : IClock
{
    /// <inheritdoc/>
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Build and commit**

`Assistant.Contracts` has been an empty project since the reset; adding two files to it needs no
csproj change. Verify with `dotnet build` — expect `0 Warning(s)`, `0 Error(s)`, and the existing
17 unit / 16 integration tests still passing. No new tests yet.

```bash
git add src/
git commit -m "feat: add a result type and an injectable clock"
```

---

## Task 2: `UpdateAsync`, `ITaskService`, and `TaskService`

- [ ] **Step 1: `ITaskRepository.UpdateAsync`**

Append to `src/Assistant.Interfaces/ITaskRepository.cs`:

```csharp
    /// <summary>
    /// Saves changes to an existing task.
    /// </summary>
    /// <param name="task">The task to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task UpdateAsync(ReminderTask task, CancellationToken ct);
```

And implement it in `src/Assistant.Repository/Repositories/EfTaskRepository.cs`:

```csharp
    /// <inheritdoc/>
    public async Task UpdateAsync(ReminderTask task, CancellationToken ct)
    {
        db.ReminderTasks.Update(task);
        await db.SaveChangesAsync(ct);
    }
```

`Update` rather than relying on change tracking: `FindAsync` returns a no-tracking entity, so the
context does not know about it. This is the pairing that decision was made for at F2.

- [ ] **Step 2: `ITaskService`**

`src/Assistant.Interfaces/ITaskService.cs`:

```csharp
using Assistant.Contracts;

namespace Assistant.Interfaces;

/// <summary>
/// The only type permitted to change a task.
/// </summary>
/// <remarks>
/// Models carry no behaviour, so the rules need one owner instead. Jobs, tool handlers and button
/// actions all call this; none of them touches a repository directly. The interface grows a method
/// per feature that needs one — it is a data-access surface, not a behaviour seam.
/// </remarks>
public interface ITaskService
{
    /// <summary>
    /// Records that the reminder for a task has been delivered.
    /// </summary>
    /// <param name="id">The task whose reminder was delivered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success, or the reason it was refused. Refused when no task carries the identifier, or when
    /// the task has no due time and therefore had no reminder to deliver.
    /// </returns>
    Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct);
}
```

- [ ] **Step 3: `TaskService`**

`src/Assistant.Impl/Services/TaskService.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Interfaces;

namespace Assistant.Impl.Services;

/// <summary>
/// The single writer for tasks.
/// </summary>
/// <param name="repository">Persistence for tasks.</param>
/// <param name="clock">The current instant.</param>
/// <remarks>
/// Every rule that governs a task's lifecycle lives here, because the models are anemic by design
/// and have no other enforcement point. A caller that mutated a task itself could set one field
/// without its pair — which is exactly how a task stops reminding forever.
/// </remarks>
internal sealed class TaskService(ITaskRepository repository, IClock clock) : ITaskService
{
    /// <inheritdoc/>
    public async Task<Result> MarkReminderSentAsync(Guid id, CancellationToken ct)
    {
        var task = await repository.FindAsync(id, ct);

        if (task is null)
        {
            return Result.Failure(ErrorCode.TaskNotFound);
        }

        if (task.DueAt is null)
        {
            return Result.Failure(ErrorCode.DueTimeMissing);
        }

        task.ReminderSentAt = clock.UtcNow;
        task.UpdatedAt = clock.UtcNow;
        await repository.UpdateAsync(task, ct);

        return Result.Success();
    }
}
```

- [ ] **Step 4: Registration**

Add to `src/Assistant.Impl/ImplServiceCollectionExtensions.cs`, as a second public method
alongside `AddAssistantTelegram`:

```csharp
    /// <summary>
    /// Registers the assistant's domain services.
    /// </summary>
    /// <param name="services">The container to add registrations to.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAssistantServices(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<ITaskService, TaskService>();
        return services;
    }
```

`ITaskService` is scoped because it depends on the scoped repository. `IClock` is a singleton — it
holds nothing.

Add `using Assistant.Impl.Services;` and `using Assistant.Impl.Time;` at the top.

- [ ] **Step 5: Build, then commit**

```bash
dotnet build
```

Expect `0 Warning(s)`, `0 Error(s)`. Tests come next.

```bash
git add src/
git commit -m "feat: mark a reminder sent through the single writer"
```

---

## Task 3: The three business tests

- [ ] **Step 1: Write them**

`tests/Assistant.IntegrationTests/Services/TaskServiceTests.cs`:

```csharp
using Assistant.Contracts;
using Assistant.Impl;
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Services;

/// <summary>
/// Test class for <see cref="ITaskService"/>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
[Collection(PostgresCollection.Name)]
public sealed class TaskServiceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf =
        new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider = postgres.CreateProvider();

    private ITaskService Sut => _provider.GetRequiredService<ITaskService>();

    private ITaskRepository Repository => _provider.GetRequiredService<ITaskRepository>();

    /// <inheritdoc/>
    public Task InitializeAsync() => postgres.ResetAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a due task's reminder has been delivered
    /// And it is recorded as sent
    /// Then the task is no longer due, so the reminder is never delivered twice.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskWasDue_StopsBeingDue()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: AsOf.AddHours(-1));
        await Repository.AddAsync(reminderTask, CancellationToken.None);

        // Act
        var result = await Sut.MarkReminderSentAsync(reminderTask.Id, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Empty(await Repository.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None));
    }

    /// <summary>
    /// When a task has no due time
    /// And its reminder is recorded as sent
    /// Then it is refused, because there was no reminder to deliver.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskHasNoDueTime_IsRejected()
    {
        // Arrange
        var reminderTask = BuildReminderTask(dueAt: null);
        await Repository.AddAsync(reminderTask, CancellationToken.None);

        // Act
        var result = await Sut.MarkReminderSentAsync(reminderTask.Id, CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.DueTimeMissing, result.Error);
    }

    /// <summary>
    /// When no task carries the requested identifier
    /// And its reminder is recorded as sent
    /// Then it is refused rather than silently doing nothing.
    /// </summary>
    [Fact]
    public async Task MarkReminderSentAsync_TaskDoesNotExist_IsRejected()
    {
        // Act
        var result = await Sut.MarkReminderSentAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        Assert.Equal(ErrorCode.TaskNotFound, result.Error);
    }

    private static ReminderTask BuildReminderTask(DateTimeOffset? dueAt) => new()
    {
        Id = Guid.NewGuid(),
        Title = "call the bank",
        Status = ReminderStatus.Pending,
        DueAt = dueAt,
        ReminderSentAt = null,
        CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
    };
}
```

The first test asserts a **behaviour**, not a field: the task stops appearing in the due query.
That is the property F5b's scheduler depends on, and it survives any change to how "sent" is
recorded.

`PostgresFixture.CreateProvider()` calls `AddAssistantRepository` only, so it will not resolve
`ITaskService` yet — Step 2 fixes that.

- [ ] **Step 2: Let the fixture build a full container**

`PostgresFixture.CreateProvider()` currently registers only the repository. Add the services:

```csharp
        services.AddAssistantRepository(ConnectionString);
        services.AddAssistantServices();
```

with `using Assistant.Impl;` at the top of the fixture. Nothing else changes — the Telegram
registration stays out, because no test here sends anything.

- [ ] **Step 3: Red, then green**

```bash
docker compose -f compose.test.yaml up -d --build
dotnet test tests/Assistant.IntegrationTests
```

Run this **before** Step 2 to see the red state — `InvalidOperationException: No service for type
'Assistant.Interfaces.ITaskService' has been registered.` Then apply Step 2.

Expect `0 Warning(s)`, `0 Error(s)`, 17 unit tests, **19** integration tests — 16 existing plus
the 3 here.

- [ ] **Step 4: Prove the headline test can fail**

Temporarily change `TaskService` so it stamps only `UpdatedAt` and leaves `ReminderSentAt` alone —
the exact mistake the single-writer rule exists to prevent, a mutation that sets one field without
its pair.

Expected: `MarkReminderSentAsync_TaskWasDue_StopsBeingDue` fails, because the task is still due.
The other two pass, which is correct — they are about refusals.

Revert and confirm green.

- [ ] **Step 5: Commit**

```bash
git add src/ tests/
git commit -m "test: prove a delivered reminder stops being due"
```

---

## Task 4: Record what F5a settled

- [ ] **Step 1: Backlog**

Split the F5 entry into F5a and F5b. Record: `IClock` landed in F5a rather than F5b because
stamping `UpdatedAt` deterministically requires it; `Result` and `ErrorCode` were designed here
because the spec never defines them, with `Error` nullable and no message string; `ITaskService`
starts with one method and grows per feature; and the tests assert business outcomes — the
headline one asserts the task stops being due rather than that a field changed.

Note against F5b that it still owns the collection merge and the architecture test.

- [ ] **Step 2: Verify and commit**

```bash
dotnet clean -v q
dotnet build
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expect `0 Warning(s)`, `0 Error(s)`, 17 unit, 19 integration.

```bash
git add docs/
git commit -m "docs: record the decisions F5a settled"
git push -u origin feature/f5a-task-service
```

Open the PR. Do not merge.

---

## Self-review

**Spec coverage.** §4.2 — `TaskService` is the single writer; `MarkReminderSent` on a task with no
`DueAt` is rejected; `UpdatedAt` is stamped. The remaining §4.2 rules belong to methods this
feature does not add. §3.6 — `IClock` with `SystemClock`. §7.2 — every rule here is observable, so
all three tests sit at the integration level and no fake repository is built.

**SOLID, as asked.** *Single responsibility:* `TaskService` owns task mutation rules and nothing
else; `SystemClock` owns one property. *Open/Closed:* the behaviour seams the backlog names —
`IScheduledJob`, `ITaskAction` — are untouched here; `ITaskService` is a data-access surface that
grows by adding methods, which the backlog already ruled is a modification no design avoids.
*Dependency inversion:* `TaskService` depends on `ITaskRepository` and `IClock`, never on EF Core.
*DRY:* the two guard clauses appear once each; the tests share one builder.

**YAGNI.** One interface method, two error codes, no `FakeClock`, no message string on `Result`,
no `Contracts` types beyond the two.

**Placeholder scan.** No TBDs. Task 3 Step 3 names the exact red-state exception.

**Known risk.** `UpdateAsync` uses `db.ReminderTasks.Update`, which marks every property modified.
That is correct today and writes the whole row. If a later feature needs concurrency control, this
is the method that changes — noted rather than solved, because a token nothing reads would be a
guess.
