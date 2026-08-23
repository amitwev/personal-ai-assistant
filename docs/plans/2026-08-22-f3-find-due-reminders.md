# F3 — Find the tasks that are due

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development.
> Steps use checkbox (`- [ ]`) syntax.

**Goal:** `ITaskRepository.GetDueRemindersAsync(asOfUtc, limit, ct)` returning pending, due,
undelivered tasks oldest-first, with the partial index that serves it.

**Architecture:** One more method on `EfTaskRepository`, one additive migration for
`idx_tasks_due_pending`. No service, no job — F5 owns the caller. The query is named by intent
rather than composable, so the index can be built for it (`ITaskRepository`'s own `<remarks>`).

**Tech Stack:** EF Core 10.0.11, Npgsql 10.0.3, xUnit 2.9.3, Respawn, Docker Compose.

**Spec:** `docs/design/slice-1-reminders.md` §4.3, §6.2.
**Backlog:** `docs/design/2026-08-22-slice-1-feature-backlog.md` — F3.

---

## Global Constraints

- `net10.0`; nullable enabled; warnings are errors; `CS1591` is an error in `src/` only.
- **EF Core types appear only in `Assistant.Repository`.**
- All instants UTC, `Offset == TimeSpan.Zero`, and every test instant is a literal with at most
  **six** fractional digits — `timestamptz` truncates below a microsecond.
- **Every class with arguments uses a primary constructor** (§12.5). No separate constructors.
- Plain xUnit `Assert`. `Assert.Equal(expected, actual)` — expected first.
- Every `<summary>` is three lines. Primary constructor parameters are documented on the class.
- **YAGNI:** this feature introduces nothing it does not test, with one stated exception below.
- PR budget: 1000 lines. Estimated ~200 of code and tests, plus this plan.

---

## Decisions this plan makes — review these first

### A. Spec §6.2 was wrong and is fixed in this branch

It read:

```sql
WHERE status = 0 AND due_at <= @now AND reminder_sent_at IS NULL
```

`status = 0` is `Unknown` under the current numbering. `ck_reminder_tasks_status_known` forbids
that value from ever existing, so the query as written would return **nothing, always**. It is a
leftover from the enum renumbering, which updated §4.3 (including the index predicate, which
correctly says `status = 1`) and missed §6.2. Corrected to `status = 1` — `Pending`.

### B. Ordering is asserted with `Assert.Equal`, never `Assert.Equivalent`

`Assert.Equivalent` compares collections **order-insensitively**, flat and nested — it passes on
`[1,2,3]` against `[3,2,1]`. Oldest-first is a business requirement here (the scheduler must
deliver the longest-overdue reminder first), so the ordering test compares a sequence of `Guid`s
with `Assert.Equal`, which is order-sensitive.

This is why the ordering test asserts ids rather than whole tasks: `Assert.Equal` on a sequence
of `ReminderTask` would compare by reference and fail. Ids are the identity that matters.

### C. Boundary values are microsecond-aligned, because ticks are not

`due_at <= asOf` needs the boundary probed on both sides. One tick either side of `asOf` would be
meaningless: Postgres stores microseconds and truncates the rest, so `asOf.AddTicks(1)` and
`asOf` are the *same stored value*. The boundary is therefore probed at **±10 ticks = ±1
microsecond**, the smallest difference the column can actually hold.

This was measured at F2: a value with sub-microsecond precision came back 7 ticks short.

### D. The partial index ships without a test, deliberately

`idx_tasks_due_pending` is the one thing here that YAGNI would otherwise exclude — it changes no
behaviour, only performance, and at single-user volume nothing needs it yet.

It ships anyway because spec §4.3 mandates it and because its `WHERE` clause is executable
documentation of the query's shape: the index and the query must agree, and having both in the
repository makes a later divergence visible.

**No test asserts it exists.** A test querying `pg_indexes` for a name we just wrote in a
migration is a change-detector — the same defect that got F1's column-list test deleted. Whether
Postgres *uses* it is an `EXPLAIN` question about performance, which this project has no budget
for and no volume to justify.

### E. `limit` is not validated

No equivalence class for `limit <= 0`, because no caller passes one — F5 passes a constant.
Postgres treats `LIMIT 0` as "no rows" and rejects a negative, both of which are fine failure
modes for a bug that does not exist yet. Validation arrives if and when a caller can produce it.

---

## What F3 does NOT include, and why

| Excluded | Returns at |
| :--- | :--- |
| `DueReminderJob`, `ReminderScheduler`, `IClock` | F5 |
| Collapsing >24h-overdue reminders into one summary (spec §6.2) | F5 — it is the job's rendering decision, not the query's |
| `delivery_attempts` and the retry cap entering the query (spec §6.2) | F11, with the column |
| `UpdateAsync` / marking a reminder sent | F5 |
| Any assertion that the index is used | Not in slice 1 (Decision D) |

---

## File Structure

| Path | Responsibility |
| :--- | :--- |
| `src/Assistant.Interfaces/ITaskRepository.cs` | **Modify.** Re-add `GetDueRemindersAsync`. |
| `src/Assistant.Repository/Configurations/ReminderTaskConfiguration.cs` | **Modify.** Add the partial index. |
| `src/Assistant.Repository/Repositories/EfTaskRepository.cs` | **Modify.** Implement the query. |
| `src/Assistant.Repository/Migrations/` | **Generated.** One additive migration. |
| `tests/Assistant.IntegrationTests/Repositories/DueReminderQueryTests.cs` | **Create.** Six tests. |
| `docs/design/slice-1-reminders.md` | **Modified already** — §6.2 `status = 0` → `status = 1`. |

**Interfaces produced:**
- `ITaskRepository.GetDueRemindersAsync(DateTimeOffset asOfUtc, int limit, CancellationToken ct)`
  → `Task<IReadOnlyList<ReminderTask>>`

---

## Test design

Six tests in a new class, so `TaskRepositoryTests` stays about `AddAsync`/`FindAsync`.

| Test | Kind | What it documents |
| :--- | :--- | :--- |
| `GetDueRemindersAsync_DueAtAroundNow_ReturnsOnlyWhatIsDue` | `[Theory]` ×3 | `due_at <= asOf` is inclusive, probed at ±1µs |
| `GetDueRemindersAsync_TaskNotPending_ReturnsNothing` | `[Theory]` ×2 | Completed and Cancelled are excluded |
| `GetDueRemindersAsync_TaskHasNoDueTime_ReturnsNothing` | `[Fact]` | A task with no deadline never reminds |
| `GetDueRemindersAsync_ReminderAlreadySent_ReturnsNothing` | `[Fact]` | `reminder_sent_at` is the idempotency key |
| `GetDueRemindersAsync_SeveralDue_ReturnsOldestFirst` | `[Fact]` | Ordering — `Assert.Equal` on a sequence |
| `GetDueRemindersAsync_MoreDueThanLimit_ReturnsOldestWithinLimit` | `[Fact]` | The limit takes the oldest, not an arbitrary subset |

**Equivalence classes.** Eligibility is the conjunction of three independent conditions, so each
gets its own ineligible class: status (Pending vs not), due time (null, at-or-before `asOf`,
after `asOf`), delivery (sent vs not). `Completed` and `Cancelled` share the "not Pending" class
but are cheap to cover as a `[Theory]`, which is what the skill prefers over a second `[Fact]`.

**Boundary values.** `due_at` at `asOf - 1µs`, exactly `asOf`, and `asOf + 1µs`. See Decision C.

**Deliberately not tested, and why:**

| Not tested | Reason |
| :--- | :--- |
| A task overdue by days is still returned | Same equivalence class as `asOf - 1µs`. The ordering test already seeds tasks hours apart, so catch-up is exercised. |
| The index exists or is used | Decision D. |
| `limit <= 0` | Decision E. |
| Every property survives the read | F2's round-trip owns that. This query returns the same mapping. |

---

## Task 1: The query, the index, and the tests

**Files:**
- Modify: `src/Assistant.Interfaces/ITaskRepository.cs`
- Modify: `src/Assistant.Repository/Configurations/ReminderTaskConfiguration.cs`
- Modify: `src/Assistant.Repository/Repositories/EfTaskRepository.cs`
- Create: `tests/Assistant.IntegrationTests/Repositories/DueReminderQueryTests.cs`
- Generated: `src/Assistant.Repository/Migrations/`

**Interfaces:**
- Consumes: `PostgresFixture.CreateProvider()`, `PostgresFixture.ResetAsync()`,
  `PostgresCollection.Name` (F1/F2); `ITaskRepository.AddAsync` (F2, used for arrangement).
- Produces: `ITaskRepository.GetDueRemindersAsync`.

- [ ] **Step 1: Re-add the interface method**

Append to `ITaskRepository`, after `FindAsync`:

```csharp
    /// <summary>
    /// Returns pending tasks that are due and whose reminder has not yet been delivered.
    /// </summary>
    /// <param name="asOfUtc">The instant to treat as "now".</param>
    /// <param name="limit">Maximum number of tasks to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Tasks ordered by due time, oldest first. There is no lower bound on the due time, so a
    /// task missed during an outage is still returned once the process is running again.
    /// </returns>
    Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct);
```

- [ ] **Step 2: Write the failing tests**

Create `tests/Assistant.IntegrationTests/Repositories/DueReminderQueryTests.cs`:

```csharp
using Assistant.IntegrationTests.Infrastructure;
using Assistant.Interfaces;
using Assistant.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Assistant.IntegrationTests.Repositories;

/// <summary>
/// Test class for <see cref="ITaskRepository.GetDueRemindersAsync"/>.
/// </summary>
/// <param name="postgres">The shared database fixture.</param>
/// <remarks>
/// Boundaries are probed at one microsecond, not one tick. Postgres <c>timestamptz</c> stores
/// microseconds and truncates below that, so a one-tick difference is not a difference at all
/// once the row is written.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DueReminderQueryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int TicksPerMicrosecond = 10;
    private const int NoLimit = 100;

    private static readonly DateTimeOffset AsOf =
        new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider = postgres.CreateProvider();

    private ITaskRepository Sut => _provider.GetRequiredService<ITaskRepository>();

    public Task InitializeAsync() => postgres.ResetAsync();

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// When a pending task's due time sits either side of the current instant
    /// And due reminders are requested as of that instant
    /// Then it is returned only when its due time has arrived.
    /// </summary>
    [Theory]
    [InlineData(-TicksPerMicrosecond, 1)]
    [InlineData(0, 1)]
    [InlineData(TicksPerMicrosecond, 0)]
    public async Task GetDueRemindersAsync_DueAtAroundNow_ReturnsOnlyWhatIsDue(
        int ticksFromAsOf, int expectedCount)
    {
        // Arrange
        var reminderTask = BuildReminderTask();
        reminderTask.DueAt = AsOf.AddTicks(ticksFromAsOf);
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Equal(expectedCount, result.Count);
    }

    /// <summary>
    /// When a due task is no longer pending
    /// And due reminders are requested
    /// Then it is not returned.
    /// </summary>
    [Theory]
    [InlineData(ReminderStatus.Completed)]
    [InlineData(ReminderStatus.Cancelled)]
    public async Task GetDueRemindersAsync_TaskNotPending_ReturnsNothing(ReminderStatus status)
    {
        // Arrange
        var reminderTask = BuildReminderTask();
        reminderTask.Status = status;
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When a pending task has no due time
    /// And due reminders are requested
    /// Then it is not returned.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_TaskHasNoDueTime_ReturnsNothing()
    {
        // Arrange
        var reminderTask = BuildReminderTask();
        reminderTask.DueAt = null;
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When a due task has already had its reminder delivered
    /// And due reminders are requested
    /// Then it is not returned.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_ReminderAlreadySent_ReturnsNothing()
    {
        // Arrange
        var reminderTask = BuildReminderTask();
        reminderTask.ReminderSentAt = AsOf;
        await SaveAsync(reminderTask);

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// When several tasks are due
    /// And due reminders are requested
    /// Then they are returned oldest due time first.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_SeveralDue_ReturnsOldestFirst()
    {
        // Arrange
        var oldest = BuildReminderTask();
        oldest.DueAt = AsOf.AddHours(-3);
        var middle = BuildReminderTask();
        middle.DueAt = AsOf.AddHours(-2);
        var newest = BuildReminderTask();
        newest.DueAt = AsOf.AddHours(-1);

        await SaveAsync(middle);
        await SaveAsync(newest);
        await SaveAsync(oldest);

        var expected = new[] { oldest.Id, middle.Id, newest.Id };

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, NoLimit, CancellationToken.None);

        // Assert
        Assert.Equal(expected, result.Select(task => task.Id));
    }

    /// <summary>
    /// When more tasks are due than the limit allows
    /// And due reminders are requested
    /// Then the oldest are returned, up to the limit.
    /// </summary>
    [Fact]
    public async Task GetDueRemindersAsync_MoreDueThanLimit_ReturnsOldestWithinLimit()
    {
        // Arrange
        var oldest = BuildReminderTask();
        oldest.DueAt = AsOf.AddHours(-3);
        var middle = BuildReminderTask();
        middle.DueAt = AsOf.AddHours(-2);
        var newest = BuildReminderTask();
        newest.DueAt = AsOf.AddHours(-1);

        await SaveAsync(newest);
        await SaveAsync(oldest);
        await SaveAsync(middle);

        var expected = new[] { oldest.Id, middle.Id };

        // Act
        var result = await Sut.GetDueRemindersAsync(AsOf, 2, CancellationToken.None);

        // Assert
        Assert.Equal(expected, result.Select(task => task.Id));
    }

    private static ReminderTask BuildReminderTask() => new()
    {
        Id = Guid.NewGuid(),
        Title = "call the bank",
        Status = ReminderStatus.Pending,
        DueAt = AsOf.AddHours(-1),
        ReminderSentAt = null,
        CreatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 8, 20, 9, 15, 30, TimeSpan.Zero),
    };

    /// <summary>
    /// Saves a task through a provider of its own, then disposes it.
    /// </summary>
    /// <param name="reminderTask">The task to save.</param>
    /// <returns>A task that completes once the row has been written.</returns>
    private async Task SaveAsync(ReminderTask reminderTask)
    {
        await using var writer = postgres.CreateProvider();
        await writer.GetRequiredService<ITaskRepository>()
            .AddAsync(reminderTask, CancellationToken.None);
    }
}
```

The two ordering tests seed rows in a **different order from the expected result** on purpose. If
they were inserted oldest-first, a query with no `ORDER BY` could pass by accident.

- [ ] **Step 3: Run and watch them fail**

```bash
docker compose -f compose.test.yaml up -d
dotnet test tests/Assistant.IntegrationTests
```

Expected: compilation fails — `GetDueRemindersAsync` is on the interface but `EfTaskRepository`
does not implement it, which is `CS0535`. That is the red state.

- [ ] **Step 4: Implement the query**

Add to `EfTaskRepository`:

```csharp
    /// <inheritdoc/>
    public async Task<IReadOnlyList<ReminderTask>> GetDueRemindersAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct) =>
        await db.ReminderTasks
            .AsNoTracking()
            .Where(t => t.Status == ReminderStatus.Pending
                        && t.DueAt != null
                        && t.DueAt <= asOfUtc
                        && t.ReminderSentAt == null)
            .OrderBy(t => t.DueAt)
            .Take(limit)
            .ToListAsync(ct);
```

An earlier draft of this plan included `t.DueAt != null` here and argued it was worth keeping for
readability. It was removed after mutation testing: deleting that clause killed no test, because
SQL three-valued logic already excludes NULL from `due_at <= @now`. It was untested code, and
while it was present no single-clause mutation could kill
`GetDueRemindersAsync_TaskHasNoDueTime_ReturnsNothing` — two clauses each excluded nulls
independently. Removing it makes that test the sole guard of null exclusion.

- [ ] **Step 5: Add the partial index**

In `ReminderTaskConfiguration.Configure`, after the property configuration:

```csharp
        builder.HasIndex(x => x.DueAt)
            .HasDatabaseName("idx_tasks_due_pending")
            .HasFilter("status = 1 AND reminder_sent_at IS NULL");
```

The filter is raw SQL against **column** names, not property names — `status`, not `Status`. It
must match spec §4.3 exactly.

- [ ] **Step 6: Generate the migration**

```bash
dotnet ef migrations add AddDueReminderIndex \
  --project src/Assistant.Repository \
  --startup-project src/Assistant.Worker
```

Read the generated `Up` before continuing. It must contain exactly one `CreateIndex` with the
filter and nothing else. If it contains a table rebuild or column changes, stop — something
drifted, and the plan needs revisiting rather than the migration being committed.

- [ ] **Step 7: Run and watch them pass**

```bash
dotnet build
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, **14** integration tests. The six test methods here
produce nine cases — the two `[Theory]` tests contribute 3 and 2, the four `[Fact]` tests one
each — on top of the 5 from F1 and F2.

- [ ] **Step 8: Prove the ordering test can fail**

Temporarily change `.OrderBy(t => t.DueAt)` to `.OrderByDescending(t => t.DueAt)` and run.

Expected: both `GetDueRemindersAsync_SeveralDue_ReturnsOldestFirst` and
`GetDueRemindersAsync_MoreDueThanLimit_ReturnsOldestWithinLimit` fail. If only the first fails,
the limit test is not actually pinning which rows the limit takes and should be strengthened.

Revert and confirm green.

- [ ] **Step 9: Commit**

```bash
git add src/ tests/ docs/design/slice-1-reminders.md
git commit -m "feat: find the tasks whose reminders are due"
```

---

## Task 2: Record what F3 settled

**Files:**
- Modify: `docs/design/2026-08-22-slice-1-feature-backlog.md`

- [ ] **Step 1: Update the F3 entry**

Mark it done. Record that spec §6.2's `status = 0` was a renumbering leftover, now `status = 1`;
that ordering is asserted with `Assert.Equal` because `Assert.Equivalent` is order-insensitive;
and that the index ships without a test, with Decision D's reasoning in one line.

- [ ] **Step 2: Full verification**

```bash
dotnet build
dotnet test tests/Assistant.UnitTests
dotnet test tests/Assistant.IntegrationTests
```

Expected: `0 Warning(s)`, `0 Error(s)`, 12 unit tests, 14 integration tests.

- [ ] **Step 3: Commit and push**

```bash
git add docs/design/2026-08-22-slice-1-feature-backlog.md
git commit -m "docs: record the decisions F3 settled"
git push -u origin feature/f3-find-due-reminders
```

Open the PR against `main`. Do not merge.

---

## Self-review

**Spec coverage.** §6.2's query is implemented predicate for predicate, with its `status` value
corrected. §6.2's ">24h overdue collapses into one summary" is excluded and assigned to F5, where
the job renders messages. §4.3's index is added with the same filter. §4.1 and §4.2 are untouched.

**Placeholder scan.** No TBDs. Step 6 and Step 8 branch on an observed outcome and state what to
do in each case.

**Type consistency.** `GetDueRemindersAsync(DateTimeOffset, int, CancellationToken)` →
`Task<IReadOnlyList<ReminderTask>>` matches the interface, the implementation, and all six tests.
`Sut`, `BuildReminderTask()`, and the two-provider save pattern match `TaskRepositoryTests` so
the two files read the same way.

**Known risk.** Step 5's `HasFilter` takes raw SQL that nothing validates — the same class of
defect as the check constraints in F1. If the filter names a column wrongly, the migration fails
loudly at apply time rather than silently, so the fixture catches it in setup. That is acceptable
coverage for something with no behavioural effect.
